// Inspectors and scene gizmos for YapsSocket and YapsPlug, built with
// UI Toolkit on the same elements as the windows. Gizmos are drawn at a
// fixed world size, about 5 cm.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // --- shared -----------------------------------------------------------

    static class YapsInspectorStyle
    {
        public static readonly Color HoleColour = new Color(0.25f, 0.85f, 0.95f);
        public static readonly Color RingColour = new Color(0.95f, 0.55f, 0.20f);
        public static readonly Color PlugColour = new Color(0.55f, 0.85f, 0.35f);
        public static readonly Color BadColour = new Color(0.95f, 0.30f, 0.30f);

        // The family stylesheet and skin.
        public static VisualElement Root()
        {
            var root = new VisualElement();
            root.AddToClassList("ab-root");
            root.AddToClassList("ab-inspector");
            BridgeTheme.ApplySkin(root);
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null) root.styleSheets.Add(sheet);
            return root;
        }

        // A bound control for one field, with its help line under it.
        public static VisualElement Field(SerializedProperty p, System.Reflection.FieldInfo field, string label = null,
            string from = null)
        {
            var tip = field?.GetCustomAttributes(typeof(TooltipAttribute), false).FirstOrDefault() as TooltipAttribute;
            var range = field?.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() as RangeAttribute;
            label = label ?? p.displayName;
            string help = tip != null ? tip.tooltip : null;

            VisualElement control;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float when range != null:
                {
                    var s = new Slider(label, range.min, range.max) { showInputField = true };
                    s.BindProperty(p);
                    s.AddToClassList("ab-slider");
                    control = s;
                    break;
                }
                case SerializedPropertyType.Float:
                {
                    var f = new FloatField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.Integer when range != null:
                {
                    var s = new SliderInt(label, (int) range.min, (int) range.max) { showInputField = true };
                    s.BindProperty(p);
                    s.AddToClassList("ab-slider");
                    control = s;
                    break;
                }
                case SerializedPropertyType.Integer:
                {
                    var f = new IntegerField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.Boolean:
                {
                    var t = new Toggle(label); t.BindProperty(p);
                    t.AddToClassList("ab-toggle");
                    t.style.flexGrow = 1;
                    control = t;
                    break;
                }
                case SerializedPropertyType.String:
                {
                    var f = new TextField(label); f.BindProperty(p); control = f; break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var f = new ObjectField(label)
                    {
                        objectType = field != null ? field.FieldType : typeof(Object),
                        allowSceneObjects = true,
                    };
                    f.BindProperty(p);
                    control = f;
                    break;
                }
                default:
                {
                    var f = new PropertyField(p, label); control = f; break;
                }
            }
            if (p.propertyType != SerializedPropertyType.Boolean) control.AddToClassList("ab-field");
            control.tooltip = help;

            // A chip per system to the control's right.
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            line.Add(control);
            if (!string.IsNullOrEmpty(from))
            {
                foreach (var s in from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var chip = BridgeElements.Chip(s, SystemColour(s), true, null, false, false);
                    chip.AddToClassList("ab-from");
                    chip.tooltip = SystemName(s);
                    line.Add(chip);
                }
            }

            var box = new VisualElement();
            box.Add(line);
            if (help != null) box.Add(BridgeElements.Hint(help));
            return box;
        }

        // The four systems, by colour.
        public static Color SystemColour(string system)
        {
            switch (system)
            {
                case "DPS": return new Color(0.70f, 0.50f, 0.95f);
                case "TPS": return new Color(0.30f, 0.78f, 0.72f);
                case "SPS": return new Color(0.95f, 0.62f, 0.28f);
                default: return PlugColour;
            }
        }

        public static string SystemName(string system)
        {
            switch (system)
            {
                case "DPS": return "From Raliv's Dynamic Penetration System";
                case "TPS": return "From Thry's Penetration System";
                case "SPS": return "From VRCFury's Super Plug Shader";
                default: return "YAPS's own — none of the others had it";
            }
        }

        // The material properties an animation may drive, by the name the
        // Animation window wants after "Material.".
        public const string AnimatablePlugProperties =
            "_YAPS_Enabled  (deform on, 0 or 1)\n" +
            "_YAPS_Curvature, _YAPS_ReCurvature, _YAPS_EntranceStiffness  (shape at rest)\n" +
            "_YAPS_Squeeze, _YAPS_SqueezeDistance, _YAPS_Bulge, _YAPS_BulgeDistance  (inside a socket)\n" +
            "_YAPS_IdleLength, _YAPS_IdleWidth, _YAPS_WriggleStrength, _YAPS_WriggleSpeed  (out of a socket)\n" +
            "_YAPS_PumpStrength, _YAPS_PumpSpeed, _YAPS_PumpWidth  (motion inside a socket)\n" +
            "_YAPS_BezierSmoothness, _YAPS_BezierStart, _YAPS_SmoothStart, _YAPS_MinimumSocketDistance  (the bend)\n" +
            "_YAPS_TaperStart, _YAPS_TaperEnd, _YAPS_Overrun  (past the opening)\n" +
            "_YAPS_BakeScale, _YAPS_BakeGirth  (the plug's size, wired at Bake from bone scale)\n" +
            "_YAPS_ShapeWeights .. _YAPS_ShapeWeights4  (its baked blendshapes, wired at Bake)";

        public const string AnimatableSocketProperties =
            "_YAPS_SocketPower  (shape strength, 0 is off)\n" +
            "_YAPS_SocketShapeStart .. _YAPS_SocketShapeStart4, _YAPS_SocketShapeFade .. _YAPS_SocketShapeFade4  (sixteen shapes, four per float4, x y z w)";

        public static Button Button(string text, System.Action act)
        {
            var b = new UnityEngine.UIElements.Button(act) { text = text };
            b.AddToClassList("ab-btn");
            return b;
        }
    }

    // --- preview -----------------------------------------------------------

    // Turns a socket's preview on and off for the inspector and the window.
    // Spawns a test plug when the scene has no baked plug. Repaints the
    // scene view while a preview is on or a plug is selected.
    static class YapsPreview
    {
        public const string PlugName = "YAPS Preview Plug";
        static bool _animating;

        public static void Set(YapsSocket socket, bool on, bool spawnPlugIfNone = true)
        {
            if (socket == null) return;
            socket.preview = on;
            if (on && spawnPlugIfNone && CountBakedPlugs() == 0) Spawn(socket);
            if (!on) { Remove(); YapsShapeSim.Release(socket); }
            socket.PreviewTick();
            Animate(on);
            SceneView.RepaintAll();
        }

        public static void Animate(bool on)
        {
            if (on == _animating) return;
            _animating = on;
            EditorApplication.update -= Tick;
            if (on) EditorApplication.update += Tick;
        }

        static void Tick()
        {
            // Stop once nothing needs it.
            var sockets = Object.FindObjectsOfType<YapsSocket>();
            bool previewing = sockets.Any(s => s.preview);
            bool plugSelected = Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<YapsPlug>() != null;
            if (!previewing && !plugSelected) { Animate(false); return; }
            // The plug's tip drives the previewing socket's shapes.
            foreach (var s in sockets)
                if (s.preview) YapsShapeSim.FollowPlugs(s);
            SceneView.RepaintAll();
        }

        public static int CountBakedPlugs()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") > 0) { n++; break; }
            return n;
        }

        // The first baked plug in the scene, as a frame: base and forward.
        // The markers object sits on the measured frame; the plug's own
        // transform is the fallback for a plug made without one.
        public static bool FirstBakedPlugFrame(out Vector3 origin, out Vector3 forward, out Vector3 up, out float length)
        {
            origin = Vector3.zero; forward = Vector3.forward; up = Vector3.up; length = 0.25f;
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>())
                if (PlugFrame(plug, out origin, out forward, out up, out length)) return true;
            return false;
        }

        // One baked plug as a frame; false when it has no live bake.
        public static bool PlugFrame(YapsPlug plug, out Vector3 origin, out Vector3 forward, out Vector3 up, out float length)
        {
            origin = Vector3.zero; forward = Vector3.forward; up = Vector3.up; length = 0.25f;
            var r = plug != null ? plug.Target : null;
            if (r == null) return false;
            var mat = r.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty("_YAPS_Bake")
                && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") > 0);
            if (mat == null) return false;
            var markers = plug.transform.Find("YAPS Markers");
            var frame = markers ?? plug.transform;
            origin = frame.position; forward = frame.forward; up = frame.up;
            length = mat.HasProperty("_YAPS_Length") ? Mathf.Max(mat.GetFloat("_YAPS_Length"), 0.05f) : 0.25f;
            if (markers == null && r is SkinnedMeshRenderer && plug.rootBone != null)
            {
                origin = plug.rootBone.position; forward = plug.rootBone.forward; up = plug.rootBone.up;
            }
            // The bake scale stretches the shaft; the tip goes with it.
            if (mat.HasProperty("_YAPS_BakeScale")) length *= Mathf.Max(mat.GetFloat("_YAPS_BakeScale"), 0.01f);
            return true;
        }

        // A test plug in front of the socket, engaged but not swallowed.
        static void Spawn(YapsSocket socket)
        {
            if (GameObject.Find(PlugName) != null) return;
            // Keep the socket selected; its inspector holds the stop button.
            // For shapes on another mesh the game measures depth in reaches,
            // so the test plug is one reach long: all the way in reads 1.
            bool contact = socket.renderer != null && socket.shapes.Count > 0
                           && !YapsNativeBuilder.MeshIsTheSocket(socket.renderer, socket.transform);
            float length = contact ? YapsSocketReactions.ReachOf(socket) : 0.25f;
            var go = YapsNativeBuilder.BuildTestPlug(null, select: false, length: length);
            if (go == null) return;
            go.name = PlugName;
            var t = socket.transform;
            // Base a little more than its length out along the socket's
            // forward, pointing back at it: the tip just short of the plane.
            go.transform.position = t.position + t.forward * (length + 0.05f);
            go.transform.rotation = Quaternion.LookRotation(-t.forward, t.up);
            EditorGUIUtility.PingObject(go);

            // Say when it did not bake; the console alone was missed.
            var plug = go.GetComponent<YapsPlug>();
            bool baked = plug != null && plug.Target != null
                         && plug.Target.sharedMaterials.Any(m => m != null && m.HasProperty("_YAPS_Bake"));
            if (!baked)
            {
                EditorUtility.DisplayDialog("YAPS preview",
                    "The test plug was placed but did not bake, so it will not bend. The Console has the " +
                    "reason on a [YAPS] line — usually the shader could not be patched.", "OK");
            }
        }

        // The preview plug's mesh, material and bake go with it. The shader copy stays.
        static void Remove()
        {
            var existing = GameObject.Find(PlugName);
            if (existing == null) return;
            var doomed = new List<string>();
            var mf = existing.GetComponent<MeshFilter>();
            var mr = existing.GetComponent<MeshRenderer>();
            if (mf != null && mf.sharedMesh != null) doomed.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
            if (mr != null)
            {
                foreach (var m in mr.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null)
                        doomed.Add(AssetDatabase.GetAssetPath(m.GetTexture("_YAPS_Bake")));
                    doomed.Add(AssetDatabase.GetAssetPath(m));
                }
            }
            Object.DestroyImmediate(existing);
            foreach (var path in doomed)
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith(YapsNativeBuilder.OutputRoot)) continue;
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    // Moves a socket's shapes on its mesh, in the editor, from a depth: the
    // slider on the socket's inspector, or the previewing plug's tip. The
    // same stage maths the built reactions and the socket shader use, so
    // what it shows is what ships. Nothing is saved: every weight it
    // touches goes back when the test ends, the socket is deselected, play
    // mode starts or scripts reload.
    static class YapsShapeSim
    {
        class Held { public SkinnedMeshRenderer Renderer; public readonly Dictionary<int, float> Original = new Dictionary<int, float>(); }
        static readonly Dictionary<int, Held> _held = new Dictionary<int, Held>();
        // The test slider's position per socket, so a rebuilt inspector keeps it.
        public static readonly Dictionary<int, float> Slider = new Dictionary<int, float>();
        // The depth last shown per socket, for the slider to read back.
        static readonly Dictionary<int, float> _depth = new Dictionary<int, float>();

        static YapsShapeSim()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) ReleaseAll(); };
        }

        public static float DepthOf(YapsSocket socket) => socket != null && _depth.TryGetValue(socket.GetInstanceID(), out var d) ? d : 0f;
        public static bool Holding(YapsSocket socket) => socket != null && _held.ContainsKey(socket.GetInstanceID());

        // A shape's weight as the author left it, seen past any test that
        // is holding the mesh right now.
        public static float AuthoredWeight(SkinnedMeshRenderer renderer, int shapeIndex)
        {
            foreach (var held in _held.Values)
                if (held.Renderer == renderer && held.Original.TryGetValue(shapeIndex, out float w)) return w;
            return renderer.GetBlendShapeWeight(shapeIndex);
        }

        // The same maths as the built layer: each shape opens from its
        // authored weight to full, scaled by the strength.
        public static void Apply(YapsSocket socket, float depth)
        {
            if (socket == null || socket.renderer == null || socket.renderer.sharedMesh == null) return;
            var r = socket.renderer;
            var mesh = r.sharedMesh;
            int id = socket.GetInstanceID();
            if (!_held.TryGetValue(id, out var held) || held.Renderer != r)
            {
                Release(socket);
                held = new Held { Renderer = r };
                _held[id] = held;
            }
            _depth[id] = depth;
            foreach (var s in socket.shapes)
            {
                if (s == null || string.IsNullOrEmpty(s.blendshape)) continue;
                int i = mesh.GetBlendShapeIndex(s.blendshape);
                if (i < 0) continue;
                if (!held.Original.ContainsKey(i)) held.Original[i] = r.GetBlendShapeWeight(i);
                float opening = Mathf.Clamp01((depth - s.startsAt) / Mathf.Max(0.01f, s.fadeOver)) * Mathf.Clamp01(socket.shapePower);
                r.SetBlendShapeWeight(i, Mathf.Lerp(held.Original[i], 100f, opening));
            }
        }

        public static void Release(YapsSocket socket)
        {
            if (socket != null) Release(socket.GetInstanceID());
        }

        static void Release(int id)
        {
            _depth.Remove(id);
            if (!_held.TryGetValue(id, out var held)) return;
            _held.Remove(id);
            if (held.Renderer == null) return;
            foreach (var kv in held.Original) held.Renderer.SetBlendShapeWeight(kv.Key, kv.Value);
        }

        public static void ReleaseAll()
        {
            foreach (int id in _held.Keys.ToList()) Release(id);
        }

        // While a socket previews, the deepest plug's tip sets its depth:
        // penetration past the socket plane, in plug lengths for a mesh of
        // the socket's own (the shader's measure), in reaches for any other
        // mesh (the contact's: the tip's position in the trigger box). A
        // tip in front of the plane is depth 0, as the game reads it. No
        // plug about, no change.
        public static void FollowPlugs(YapsSocket socket)
        {
            if (socket == null || socket.renderer == null || socket.shapes.Count == 0) return;
            float depth = DepthFromPlugs(socket);
            if (depth < 0f) { Release(socket); return; }
            Apply(socket, depth);
        }

        public static float DepthFromPlugs(YapsSocket socket)
        {
            bool own = YapsNativeBuilder.MeshIsTheSocket(socket.renderer, socket.transform);
            float best = -1f;
            var at = socket.transform;
            YapsSocketReactions.TriggerBox(socket, out var offset, out var size);
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>())
            {
                if (!YapsPreview.PlugFrame(plug, out var origin, out var forward, out _, out float length)) continue;
                var tip = origin + forward * length;
                var local = at.InverseTransformPoint(tip);   // z along the socket's forward, out of it
                float depth;
                if (own)
                {
                    depth = -local.z / Mathf.Max(length, 0.01f);
                    // More than a plug length short of the plane: not about.
                    if (depth < -1f) continue;
                }
                else
                {
                    // In the trigger box, or just in front of it: the game
                    // reads 0 there and 0 at the plane.
                    var rel = local - offset;
                    bool inside = Mathf.Abs(rel.x) <= size.x * 0.5f && Mathf.Abs(rel.y) <= size.y * 0.5f
                                  && rel.z >= -size.z * 0.5f && rel.z <= size.z * 0.5f + size.z;
                    if (!inside) continue;
                    depth = -local.z / size.z;
                }
                best = Mathf.Max(best, Mathf.Clamp01(depth));
            }
            return best;
        }
    }

    // --- socket ------------------------------------------------------------

    [CustomEditor(typeof(YapsSocket))]
    public class YapsSocketEditor : Editor
    {
        VisualElement _root;
        IVisualElementScheduledItem _reactionRefresh;

        // Ticks the preview on every scene repaint while selected.
        void OnSceneGUI()
        {
            var socket = target as YapsSocket;
            if (socket != null && socket.preview) { socket.PreviewTick(); YapsPreview.Animate(true); }
        }

        // The test slider's shapes go back on deselect; a preview keeps
        // driving them until it stops.
        void OnDisable()
        {
            var socket = target as YapsSocket;
            if (socket != null && !socket.preview) YapsShapeSim.Release(socket);
        }

        // A shape edit after Build changes the reactions layer too, once
        // the edit settles, so the animator says what the socket says.
        void ReactionsChanged(YapsSocket socket)
        {
            _reactionRefresh?.Pause();
            if (!YapsSocketReactions.Exists(socket)) return;
            _reactionRefresh = _root.schedule.Execute(() =>
            {
                if (socket != null) YapsSocketReactions.Build(socket);
            }).StartingIn(700);
        }

        // The test slider, if it is up, shows the edited stages at once.
        static void Retest(YapsSocket socket)
        {
            if (socket == null || socket.preview) return;
            if (YapsShapeSim.Slider.TryGetValue(socket.GetInstanceID(), out float d) && d > 0f) YapsShapeSim.Apply(socket, d);
        }

        // Strength reaches wherever the shapes live: the socket's material,
        // the reactions layer, and the shapes on the mesh while a test runs.
        static void PushStrength(YapsSocket socket, Material bakedMat)
        {
            if (bakedMat != null && bakedMat.HasProperty("_YAPS_SocketPower"))
            {
                bakedMat.SetFloat("_YAPS_SocketPower", socket.shapePower);
                EditorUtility.SetDirty(bakedMat);
            }
            YapsSocketReactions.SetStrength(socket);
            if (YapsShapeSim.Holding(socket)) YapsShapeSim.Apply(socket, YapsShapeSim.DepthOf(socket));
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = YapsInspectorStyle.Root();
            Rebuild();
            return _root;
        }

        // Callbacks ask for a rebuild from inside the elements about to go.
        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        // Rebuilt on structural change and bound. Sliders need nothing.
        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var socket = (YapsSocket) target;
            var so = serializedObject;
            var kindProp = so.FindProperty("kind");
            var rendererProp = so.FindProperty("renderer");
            var shapesProp = so.FindProperty("shapes");
            var powerProp = so.FindProperty("shapePower");
            var lightsProp = so.FindProperty("emitLights");
            var type = typeof(YapsSocket);

            var avatarRoot = AvatarRootOf(socket.transform);
            bool hole = kindProp.enumValueIndex == (int) YapsSocket.SocketKind.Hole;
            bool built = IsBuilt(socket.transform);

            _root.Add(BridgeElements.Banner((hole ? "Hole" : "Ring") + "  ·  " + socket.name,
                built ? "readable by DPS, TPS, SPS and YAPS plugs" : "not built — no plug can find it yet",
                built ? "YAPS" : "not built"));

            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);

            // What it is.
            var what = new BridgeElements.Card("What it is");
            what.Body.Add(BridgeElements.Choice("Kind",
                "A hole closes around the plug and stops it. A ring lets it pass straight through.",
                new[] { "Hole", "Ring" }, kindProp.enumValueIndex, i =>
                {
                    if (i == kindProp.enumValueIndex) return;
                    // One undo step for the kind and the markers it rebuilds:
                    // the hierarchy is recorded before the enum changes.
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "YAPS socket kind");
                    kindProp.enumValueIndex = i;
                    so.ApplyModifiedProperties();
                    YapsSocketBuilder.ApplyKind(socket);
                    Undo.CollapseUndoOperations(group);
                    RebuildLater();
                }));
            what.Body.Add(BridgeElements.Hint(hole
                ? "A hole closes around the plug and stops it — a mouth, a pussy, an anus."
                : "A ring lets the plug pass straight through — a hand, thighs, a foot."));
            body.Add(what);

            // Shapes.
            var opens = new BridgeElements.Card("Opens as a plug goes in");
            opens.Body.Add(BridgeElements.Hint(
                "Pick the mesh whose shapes should open (usually the body), then up to sixteen of its " +
                "shapes, several per depth if you like. The entry opens as the plug arrives; each later " +
                "one starts deeper. Depths are fractions of the plug's length, and they stack. Built by " +
                "\"Bake every plug and verify\": a mesh of the socket's own opens in its shader; any other " +
                "mesh opens through a contact on the socket and a layer in your animator."));

            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            // The user's choice, never a guess: a guess written on every
            // draw made "None" impossible and dirtied a socket on selection.
            var current = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            if (current != null && !renderers.Contains(current)) renderers.Insert(0, current);
            var rNames = new List<string> { "None — bend plugs, play no shape" };
            rNames.AddRange(renderers.Select(r => $"{r.name}   ·   {r.sharedMesh.blendShapeCount} shapes"));
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            var meshPopup = new PopupField<string>("Mesh", rNames, rIndex + 1);
            meshPopup.AddToClassList("ab-field");
            meshPopup.RegisterValueChangedCallback(e =>
            {
                int picked = meshPopup.index - 1;
                YapsShapeSim.Release(socket);
                rendererProp.objectReferenceValue = picked >= 0 && picked < renderers.Count ? renderers[picked] : null;
                so.ApplyModifiedProperties();
                ReactionsChanged(socket);
                RebuildLater();
            });
            bool contactRoute = current != null && !YapsNativeBuilder.MeshIsTheSocket(current, socket.transform);
            if (contactRoute)
            {
                float reach = YapsSocketReactions.ReachOf(socket);
                string reachFrom = socket.depthReach > 0f ? "set below"
                    : YapsSocketReactions.LongestPlugOn(avatarRoot) > 0f ? "the length of the longest plug on this avatar"
                    : "the default, no plug on this avatar yet";
                opens.Body.Add(new HelpBox(
                    $"\"{current.name}\" is not a mesh of the socket's own, so Build drives these shapes through a contact: " +
                    "a depth trigger on the socket reads a plug's tip pointer and a layer in your animator plays the " +
                    "stages from it. Works with TPS, SPS and YAPS plugs; a DPS light-only plug has no pointer. " +
                    $"Free: every client computes contacts itself. A contact cannot know a plug's length, so depth 1 " +
                    $"is {reach:0.00} m in ({reachFrom}); the depths below are fractions of that.", HelpBoxMessageType.Info));
            }
            opens.Body.Add(meshPopup);

            if (current != null)
            {
                var mesh = current.sharedMesh;
                var shapeNames = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
                var options = new List<string> { "— pick a shape —" };
                options.AddRange(shapeNames);
                // Rows are named by their depth; several rows may share one.
                string StageName(int i, float at) => i == 0 && at <= 0.001f ? "Entry" : $"At {at:0.00}";
                var tint = hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;

                for (int i = 0; i < shapesProp.arraySize && i < YapsBaker.MaxShapes; i++)
                {
                    int index = i;
                    var row = shapesProp.GetArrayElementAtIndex(i);
                    var name = row.FindPropertyRelative("blendshape");
                    var start = row.FindPropertyRelative("startsAt");
                    var fade = row.FindPropertyRelative("fadeOver");
                    int sIndex = shapeNames.IndexOf(name.stringValue);

                    var stage = new VisualElement();
                    stage.AddToClassList("ab-stage");
                    var stripe = new VisualElement();
                    stripe.AddToClassList("ab-report-stripe");
                    stripe.style.backgroundColor = Color.Lerp(tint, Color.white, Mathf.Clamp01(0.6f - start.floatValue * 0.6f));
                    stage.Add(stripe);
                    var inner = new VisualElement();
                    inner.style.flexGrow = 1;
                    stage.Add(inner);

                    var head = new VisualElement();
                    head.AddToClassList("ab-row");
                    head.style.alignItems = Align.Center;
                    var shapePopup = new PopupField<string>(StageName(i, start.floatValue), options, sIndex + 1);
                    shapePopup.AddToClassList("ab-field");
                    shapePopup.style.flexGrow = 1;
                    shapePopup.style.flexShrink = 1;
                    shapePopup.style.minWidth = 0;
                    shapePopup.style.width = StyleKeyword.Auto;
                    shapePopup.RegisterValueChangedCallback(e =>
                    {
                        int picked = shapePopup.index - 1;
                        // The old shape goes back before the new one moves.
                        YapsShapeSim.Release(socket);
                        name.stringValue = picked >= 0 ? shapeNames[picked] : "";
                        so.ApplyModifiedProperties();
                        ReactionsChanged(socket);
                        Retest(socket);
                    });
                    head.Add(shapePopup);
                    var remove = YapsInspectorStyle.Button("Remove", () =>
                    {
                        YapsShapeSim.Release(socket);
                        shapesProp.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        ReactionsChanged(socket);
                        RebuildLater();
                    });
                    remove.style.flexShrink = 0;
                    remove.style.marginLeft = 4;
                    head.Add(remove);
                    inner.Add(head);

                    // Depth as one range bar.
                    float s0 = start.floatValue, e0 = Mathf.Min(1f, start.floatValue + fade.floatValue);
                    var range = new MinMaxSlider("Opens from → fully open by", s0, e0, 0f, 1f);
                    range.AddToClassList("ab-field");
                    var readout = BridgeElements.Hint($"{s0:0.00}  →  {e0:0.00}  of the plug's length");
                    range.RegisterValueChangedCallback(e =>
                    {
                        start.floatValue = e.newValue.x;
                        fade.floatValue = Mathf.Max(0.01f, e.newValue.y - e.newValue.x);
                        so.ApplyModifiedProperties();
                        readout.text = $"{e.newValue.x:0.00}  →  {e.newValue.y:0.00}  of the plug's length";
                        ReactionsChanged(socket);
                        Retest(socket);
                    });
                    inner.Add(range);
                    inner.Add(readout);
                    if (!string.IsNullOrEmpty(name.stringValue) && sIndex < 0)
                        inner.Add(new HelpBox($"\"{name.stringValue}\" is not on this mesh.", HelpBoxMessageType.Warning));
                    opens.Body.Add(stage);
                }
                if (shapesProp.arraySize < YapsBaker.MaxShapes)
                {
                    // Two ways to add: another shape at the same depth as the
                    // last row (several open together), or one deeper.
                    void AddRow(float startsAt, float fadeOver)
                    {
                        shapesProp.arraySize++;
                        var row = shapesProp.GetArrayElementAtIndex(shapesProp.arraySize - 1);
                        row.FindPropertyRelative("blendshape").stringValue = "";
                        row.FindPropertyRelative("startsAt").floatValue = startsAt;
                        row.FindPropertyRelative("fadeOver").floatValue = fadeOver;
                        so.ApplyModifiedProperties();
                        ReactionsChanged(socket);
                        RebuildLater();
                    }
                    int n = shapesProp.arraySize;
                    float lastStart = n > 0 ? shapesProp.GetArrayElementAtIndex(n - 1).FindPropertyRelative("startsAt").floatValue : 0f;
                    float lastFade = n > 0 ? shapesProp.GetArrayElementAtIndex(n - 1).FindPropertyRelative("fadeOver").floatValue : 0.3f;
                    var adders = new List<VisualElement>();
                    if (n == 0)
                    {
                        adders.Add(YapsInspectorStyle.Button("+ Add the entry shape", () => AddRow(0f, 0.3f)));
                    }
                    else
                    {
                        adders.Add(YapsInspectorStyle.Button("+ Another shape at the same depth", () => AddRow(lastStart, lastFade)));
                        adders.Add(YapsInspectorStyle.Button("+ A shape deeper in", () => AddRow(Mathf.Min(1f, lastStart + 0.25f), 0.3f)));
                    }
                    opens.Body.Add(BridgeElements.Row(adders.ToArray()));
                    opens.Body.Add(BridgeElements.Hint($"{n} of {YapsBaker.MaxShapes} shapes. Several may share a depth; each opens over its own range."));
                }
                if (shapesProp.arraySize > 0)
                {
                    var strength = YapsInspectorStyle.Field(powerProp, type.GetField("shapePower"), "Strength");
                    // Written through as it moves: the material once baked,
                    // the reactions layer once built, the test on the mesh.
                    strength.TrackPropertyValue(powerProp, p => PushStrength(socket, FindSocketMaterial(socket)));
                    opens.Body.Add(strength);
                    if (contactRoute)
                    {
                        var reachProp = so.FindProperty("depthReach");
                        var reachField = YapsInspectorStyle.Field(reachProp, type.GetField("depthReach"), "Full depth (m)");
                        reachField.TrackPropertyValue(reachProp, p => { ReactionsChanged(socket); Retest(socket); RebuildLater(); });
                        opens.Body.Add(reachField);
                    }

                    // Nothing happens in game until Build has run. Said
                    // here, where the shapes are set, with the button.
                    bool shapesBuilt = contactRoute ? YapsSocketReactions.Exists(socket) : FindSocketMaterial(socket) != null;
                    if (!shapesBuilt)
                    {
                        opens.Body.Add(new HelpBox(
                            "Not built yet: these shapes do nothing in game until the socket is built. Placing and testing " +
                            "need no build. Build this one here to try it, but the last step before an upload is still " +
                            "\"Bake every plug and verify\" in YAPS Setup: it builds every plug and socket at once and checks the lot.",
                            HelpBoxMessageType.Warning));
                        opens.Body.Add(new BridgeElements.PrimaryButton("Build this socket", () =>
                        {
                            foreach (var line in YapsNativeBuilder.BuildSocket(socket)) Debug.Log("[YAPS] " + line);
                            RebuildLater();
                        }));
                    }
                }
            }
            else if (renderers.Count == 0)
            {
                opens.Body.Add(new HelpBox(avatarRoot == null
                    ? "Put this socket under an avatar or prop and its meshes will appear here."
                    : "No skinned mesh with blendshapes under this avatar — the socket bends plugs and plays no shape.",
                    HelpBoxMessageType.Info));
            }
            body.Add(opens);

            // See it work.
            int bakedPlugs = YapsPreview.CountBakedPlugs();
            var see = new BridgeElements.Card("See it work");
            string shapesToo = contactRoute && shapesProp.arraySize > 0
                ? $" The shapes follow the plug's tip as the game would: 0 at the socket plane, 1 at {YapsSocketReactions.ReachOf(socket):0.00} m in."
                : "";
            see.Body.Add(BridgeElements.Hint(bakedPlugs > 0
                ? $"Preview bends the {bakedPlugs} YAPS plug{(bakedPlugs == 1 ? "" : "s")} in the scene toward this socket, in the editor, so you can place it and watch before uploading. Writes nothing that ships.{shapesToo}"
                : "There is no baked plug in the scene (a hidden one does not count), so Preview drops a test plug in front of this socket and bends it. Move the socket and watch it follow. The test plug goes when preview stops." +
                  (contactRoute && shapesProp.arraySize > 0 ? " It is one full depth long, so all the way in reads 1." : "") + shapesToo));
            var previewButton = new BridgeElements.PrimaryButton(
                socket.preview ? "Previewing — click to stop" : (bakedPlugs > 0 ? "Preview" : "Preview with a test plug"),
                () => { YapsPreview.Set(socket, !socket.preview); RebuildLater(); });
            see.Body.Add(previewButton);

            // The shapes, tried here: a depth slider moves them on the mesh
            // in the editor; while previewing, the plug's tip is the depth.
            if (current != null && shapesProp.arraySize > 0)
            {
                int id = socket.GetInstanceID();
                var test = new Slider("Test depth", 0f, 1f) { showInputField = true };
                test.AddToClassList("ab-field"); test.AddToClassList("ab-slider");
                var testHint = BridgeElements.Hint(socket.preview
                    ? "The preview plug is driving the shapes: move its tip into the socket and watch them open. Nothing is saved."
                    : "Moves the shapes on the mesh from a depth, here in the editor, so you can see the stages without a plug. " +
                      "Nothing is saved; they go back when you click away.");
                if (socket.preview)
                {
                    test.SetEnabled(false);
                    test.schedule.Execute(() => test.SetValueWithoutNotify(YapsShapeSim.DepthOf(socket))).Every(100);
                }
                else
                {
                    test.value = YapsShapeSim.Slider.TryGetValue(id, out float held) ? held : 0f;
                    test.RegisterValueChangedCallback(e =>
                    {
                        YapsShapeSim.Slider[id] = e.newValue;
                        YapsShapeSim.Apply(socket, e.newValue);
                        SceneView.RepaintAll();
                    });
                }
                see.Body.Add(test);
                see.Body.Add(testHint);
            }
            body.Add(see);

            // The socket-side knobs live on the material. Drawn here too once baked.
            var bakedMat = FindSocketMaterial(socket);
            if (bakedMat != null)
            {
                var opensHow = new BridgeElements.Card("How it opens");
                opensHow.Body.Add(BridgeElements.Hint(
                    "Read by the shader on this socket's mesh. Strength above scales all the shapes; each " +
                    "stage opens from its start to start + fade, as fractions of the plug's length. Depth " +
                    "comes from the plug's tracker light, or from the contact channel where there is one."));
                // One range per baked shape, named after the shape when the
                // component knows it, else by its slot.
                int baked = bakedMat.HasProperty("_YAPS_ShapeCount") ? Mathf.RoundToInt(bakedMat.GetFloat("_YAPS_ShapeCount")) : 0;
                for (int i = 0; i < Mathf.Min(baked, YapsBaker.MaxShapes); i++)
                {
                    int stage = i;
                    var (st0, fd0) = YapsNativeBuilder.ReadStage(bakedMat, i);
                    string label = i < socket.shapes.Count && !string.IsNullOrEmpty(socket.shapes[i].blendshape)
                        ? socket.shapes[i].blendshape : $"Shape {i}";
                    var mm = new MinMaxSlider(label, st0, Mathf.Min(1f, st0 + fd0), 0f, 1f);
                    mm.AddToClassList("ab-field");
                    mm.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(bakedMat, "YAPS socket shape");
                        YapsNativeBuilder.WriteStage(bakedMat, stage, e.newValue.x, e.newValue.y - e.newValue.x);
                        EditorUtility.SetDirty(bakedMat);
                    });
                    opensHow.Body.Add(mm);
                }
                opensHow.Body.Add(BridgeElements.Row(YapsInspectorStyle.Button("Open material", () => Selection.activeObject = bakedMat)));
                body.Add(opensHow);
            }

            // Advanced.
            // The socket-side knobs are material properties too.
            if (bakedMat != null)
            {
                var animate = new BridgeElements.Card("Animate it", null, false, null, 0.9f);
                animate.Body.Add(BridgeElements.Hint(
                    "The socket's shape knobs are material properties on its mesh, so an animation can drive " +
                    "them: in the Animation window pick the mesh, add Skinned Mesh Renderer ▸ Material ▸ a " +
                    "name below, and key it."));
                animate.Body.Add(new TextField { value = YapsInspectorStyle.AnimatableSocketProperties, multiline = true, isReadOnly = true });
                body.Add(animate);
            }

            var advanced = new BridgeElements.Card("Advanced");
            advanced.Body.Add(YapsInspectorStyle.Field(lightsProp, type.GetField("emitLights"), "Emit marker lights"));
            advanced.Body.Add(BridgeElements.Row(
                YapsInspectorStyle.Button(built ? "Rebuild markers" : "Build markers", () =>
                {
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Rebuild YAPS socket");
                    YapsSocketBuilder.Build(socket);
                    RebuildLater();
                }),
                YapsInspectorStyle.Button("Open YAPS Setup", YapsSetupWindow.Open)));
            var removeSocket = YapsInspectorStyle.Button("Remove this socket", () =>
            {
                var parent = socket.transform.parent;
                if (YapsRemover.Ask(socket)) Selection.activeTransform = parent;
            });
            removeSocket.style.color = BridgeTheme.Bad;
            advanced.Body.Add(BridgeElements.Row(removeSocket));
            advanced.Body.Add(BridgeElements.Hint(
                "Takes the socket out entire: its objects, its animator layer and parameter, its menu " +
                "toggle, and the socket bake on its own mesh. One undo step."));
            body.Add(advanced);

            _root.Bind(so);
        }

        // The material with this socket's baked deform.
        static Material FindSocketMaterial(YapsSocket socket)
        {
            IEnumerable<Renderer> candidates = socket.renderer != null
                ? new Renderer[] { socket.renderer }
                : AvatarRootOf(socket.transform).GetComponentsInChildren<Renderer>(true);
            foreach (var r in candidates)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.HasProperty("_YAPS_SocketPower") && m.HasProperty("_YAPS_Bake")
                        && m.GetTexture("_YAPS_Bake") != null && m.GetFloat("_YAPS_SocketPower") >= 0f
                        && (socket.renderer != null || m.GetFloat("_YAPS_SocketPower") > 0f))
                        return m;
                }
            }
            return null;
        }

        // Built means a plug can find it: a root light or a root pointer beneath.
        internal static bool IsBuilt(Transform socket)
        {
            foreach (var l in socket.GetComponentsInChildren<Light>(true))
            {
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d >= 1 && d <= 6) return true;
            }
            foreach (var p in socket.GetComponentsInChildren<ABI.CCK.Components.CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type)) continue;
                if (p.type.StartsWith("SPSLL_Socket_") || p.type.StartsWith("TPS_Orf_")) return true;
            }
            return false;
        }

        internal static Transform AvatarRootOf(Transform t)
        {
            Transform top = t;
            for (var at = t; at != null; at = at.parent)
            {
                if (at.GetComponent<Animator>() != null) return at;
                top = at;
            }
            return top;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        static void DrawSocket(YapsSocket socket, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var t = socket.transform;
            bool built = IsBuilt(t);
            bool hole = socket.kind == YapsSocket.SocketKind.Hole;
            var colour = !built ? YapsInspectorStyle.BadColour : hole ? YapsInspectorStyle.HoleColour : YapsInspectorStyle.RingColour;
            colour.a = selected ? 1f : 0.7f;

            Vector3 c = t.position, f = t.forward, u = t.up;
            // Fixed world size, scaled with the avatar.
            float scale = Mathf.Max(0.05f, (t.lossyScale.x + t.lossyScale.y + t.lossyScale.z) / 3f);
            float r = 0.05f * scale;
            float thick = selected ? 4f : 3f;

            Handles.color = colour;
            Handles.DrawWireDisc(c, f, r, thick);
            // Entry arrow, from where a plug comes.
            Handles.DrawLine(c + f * (r * 2.0f), c + f * (r * 0.6f), thick);
            Handles.ConeHandleCap(0, c + f * (r * 0.6f), Quaternion.LookRotation(-f), r * 0.5f, EventType.Repaint);
            if (hole)
            {
                // A fainter ring behind, so a hole reads as a tube.
                var faint = colour; faint.a *= 0.4f;
                Handles.color = faint;
                Vector3 back = c - f * (r * 1.4f);
                Handles.DrawWireDisc(back, f, r * 0.7f, thick * 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    var q = Quaternion.AngleAxis(i * 90f, f);
                    Handles.DrawLine(c + q * u * r, back + q * u * (r * 0.7f), thick * 0.5f);
                }
            }
            if (selected || !built)
            {
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(c + u * (r * 1.4f), (hole ? "hole" : "ring") + (built ? "" : "  — not built"), label);
            }
        }
    }

    // --- plug --------------------------------------------------------------

    [CustomEditor(typeof(YapsPlug))]
    public class YapsPlugEditor : Editor
    {
        VisualElement _root;

        // Fields grouped into three cards by their [Header].
        static readonly string[] MeshHeaders = { "Mesh", "Skinned mesh", "Measurement" };
        static readonly string[] MoveHeaders =
        {
            "Shape at rest", "Inside a socket", "Out of a socket", "Motion inside a socket", "The bend toward a socket",
        };

        // Filter and fold state, kept for the session.
        static readonly List<string> Systems = new List<string> { "All systems", "DPS", "TPS", "SPS", "YAPS" };
        static string _filter = "All systems";
        static readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();

        static bool Passes(string from)
        {
            if (_filter == "All systems") return true;
            if (string.IsNullOrEmpty(from)) return false;
            return from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries).Contains(_filter);
        }

        // The fold names its systems; an emptied fold is hidden.
        static void FinishFold(BridgeElements.Card fold, HashSet<string> systems)
        {
            if (fold == null) return;
            var ordered = new[] { "DPS", "TPS", "SPS", "YAPS" }.Where(systems.Contains).ToList();
            fold.SetSummary(ordered.Count > 0 ? "from " + string.Join(" · ", ordered) : null);
            if (fold.Body.childCount == 0) fold.style.display = DisplayStyle.None;
        }

        // A selected plug animates in the scene view.
        void OnEnable() => YapsPreview.Animate(true);

        public override VisualElement CreateInspectorGUI()
        {
            _root = YapsInspectorStyle.Root();
            Rebuild();
            return _root;
        }

        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var plug = (YapsPlug) target;
            var renderer = plug.Target;
            var baked = BakedMaterials(renderer);
            bool isBaked = baked.Count > 0;
            float len = isBaked ? baked[0].GetFloat("_YAPS_Length") : 0f;

            _root.Add(BridgeElements.Banner("Plug  ·  " + plug.name,
                renderer == null ? "no renderer — pick the mesh that bends"
                : isBaked ? $"baked  ·  {len:0.###} m  ·  {baked[0].name}" : "not baked yet — set it up, then Bake",
                isBaked ? "YAPS" : "not baked"));

            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);

            var mesh = new BridgeElements.Card("Mesh");
            var move = new BridgeElements.Card("How it moves");
            var sockets = new BridgeElements.Card("Sockets");
            body.Add(mesh); body.Add(move); body.Add(sockets);

            // Every knob is tagged with its system, and the filter shows one system.
            var filter = new PopupField<string>("Show", Systems.ToList(), Systems.IndexOf(_filter) < 0 ? 0 : Systems.IndexOf(_filter));
            filter.AddToClassList("ab-field");
            filter.RegisterValueChangedCallback(e => { _filter = e.newValue; RebuildLater(); });
            move.Body.Add(filter);
            move.Body.Add(BridgeElements.Hint(
                "Every knob is tagged with the system it comes from. Know a feature from DPS, TPS or SPS? " +
                "Pick that system and only its knobs stay. YAPS is what none of them had. Applies to Sockets below too."));

            // Fields in declaration order; a [Header] opens a section.
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            string header = "Mesh";
            VisualElement into = mesh.Body;
            BridgeElements.Card fold = null;
            var foldSystems = new HashSet<string>();
            bool first = true;
            while (it.NextVisible(false))
            {
                var field = typeof(YapsPlug).GetField(it.name);
                var h = field?.GetCustomAttributes(typeof(HeaderAttribute), false).FirstOrDefault() as HeaderAttribute;
                var from = (field?.GetCustomAttributes(typeof(YapsFromAttribute), false).FirstOrDefault() as YapsFromAttribute)?.System;
                if (h != null && h.header != header)
                {
                    FinishFold(fold, foldSystems);
                    header = h.header;
                    if (MeshHeaders.Contains(header))
                    {
                        into = mesh.Body; fold = null;
                        into.Add(BridgeElements.SubHeading(header));
                    }
                    else
                    {
                        var owner = MoveHeaders.Contains(header) ? move.Body : sockets.Body;
                        if (!_open.TryGetValue(header, out bool open)) open = true;
                        string title = header;
                        fold = new BridgeElements.Card(header, null, open, null, 0f, o => _open[title] = o);
                        fold.AddToClassList("ab-fold");
                        owner.Add(fold);
                        into = fold.Body;
                        foldSystems.Clear();
                    }
                }
                else if (first)
                {
                    into.Add(BridgeElements.SubHeading("Mesh"));
                }
                first = false;

                if (!string.IsNullOrEmpty(from))
                    foreach (var s in from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries)) foldSystems.Add(s);
                if (!Passes(from)) continue;
                into.Add(YapsInspectorStyle.Field(it.Copy(), field, null, from));
            }
            FinishFold(fold, foldSystems);

            var bake = new BridgeElements.Card("Bake");
            bake.Body.Add(BridgeElements.Hint(isBaked
                ? "The knobs above write straight to the plug's material, and the material's YAPS panel writes back here — same values, two doors. Mesh, bone and measurement changes need a re-bake."
                : "Set the mesh up, then Bake: it measures the mesh, patches the material's own shader (or falls back to YAPS Simple Lit), writes the knobs and announces the plug to every socket family."));
            bake.Body.Add(new BridgeElements.PrimaryButton(isBaked ? "Re-bake" : "Bake", () =>
            {
                var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
                string said = o.Message + (o.Notes.Count > 0 ? "  " + string.Join("  ", o.Notes) : "");
                if (o.Ok) Debug.Log("[YAPS] " + said); else Debug.LogError("[YAPS] " + said);
                RebuildLater();
            }));
            var removePlug = YapsInspectorStyle.Button("Remove this plug", () =>
            {
                var parent = plug.transform.parent;
                if (YapsRemover.Ask(plug)) Selection.activeTransform = parent;
            });
            removePlug.style.color = BridgeTheme.Bad;
            bake.Body.Add(BridgeElements.Row(YapsInspectorStyle.Button("Open YAPS Setup", YapsSetupWindow.Open), removePlug));
            if (isBaked)
            {
                bake.Body.Add(BridgeElements.Hint(
                    "Remove takes the plug out entire: the material it replaced goes back in its slot, " +
                    "the size wiring leaves your animations, the menu toggle and the markers go. One undo step."));
            }
            body.Add(bake);

            // Every knob is a material property, so every knob is animatable
            // from the avatar's own animator; the names are what to record.
            var animate = new BridgeElements.Card("Animate it", null, false, null, 0.9f);
            animate.Body.Add(BridgeElements.Hint(
                "Every knob above is a material property on this plug's mesh, so an animation can drive " +
                "it: in the Animation window pick the mesh, add Skinned Mesh Renderer (or Mesh Renderer) " +
                "▸ Material ▸ one of the names below, and key it. An erectness slider, say, takes " +
                "Curvature toward 0 and Entrance stiffness up. Size sliders and hyper toggles that scale " +
                "the bone or a blendshape are wired for you at Bake."));
            animate.Body.Add(new TextField { value = YapsInspectorStyle.AnimatablePlugProperties, multiline = true, isReadOnly = true });
            body.Add(animate);

            _root.Bind(serializedObject);

            // Knobs write through to the material. The material panel writes back.
            body.TrackSerializedObjectValue(serializedObject, so =>
            {
                var mats = BakedMaterials(plug.Target);
                if (plug.Target != renderer)
                {
                    // A new renderer that is already baked owns the knobs. Read them in.
                    if (mats.Count > 0)
                    {
                        Undo.RecordObject(plug, "YAPS plug knobs");
                        YapsNativeBuilder.ReadKnobs(plug, mats[0]);
                        EditorUtility.SetDirty(plug);
                    }
                    RebuildLater();
                    return;
                }
                // No undo record on the material: the component owns the
                // values and its own undo replays here, so recording the
                // material again would only discard the redo history.
                foreach (var m in mats)
                {
                    YapsNativeBuilder.WriteKnobs(plug, m);
                    if (plug.lengthOverride > 0) m.SetFloat("_YAPS_Length", plug.lengthOverride);
                    EditorUtility.SetDirty(m);
                }
                if ((mats.Count > 0) != isBaked) RebuildLater();
                SceneView.RepaintAll();
            });
        }

        static List<Material> BakedMaterials(Renderer renderer)
        {
            var list = new List<Material>();
            if (renderer == null) return list;
            foreach (var m in renderer.sharedMaterials)
                if (m != null && m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_Length")) list.Add(m);
            return list;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        static void DrawPlug(YapsPlug plug, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var renderer = plug.Target;
            var colour = YapsInspectorStyle.PlugColour; colour.a = selected ? 1f : 0.7f;
            Handles.color = colour;

            float length = 0f;
            if (renderer != null)
                foreach (var m in renderer.sharedMaterials)
                    if (m != null && m.HasProperty("_YAPS_Length")) { length = m.GetFloat("_YAPS_Length"); break; }

            // The markers object is the frame when built.
            var frame = plug.transform.Find("YAPS Markers") ?? plug.transform;
            Vector3 b = frame.position, f = frame.forward;
            float thick = selected ? 4f : 3f;
            float scale = Mathf.Max(0.05f, (frame.lossyScale.x + frame.lossyScale.y + frame.lossyScale.z) / 3f);

            if (length <= 0f)
            {
                Handles.DrawDottedLine(b, b + f * 0.3f * scale, 5f);
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(b + f * 0.3f * scale, "plug — not baked", label);
                return;
            }
            Vector3 tip = b + f * length * scale;
            // True length along the axis, a ring at the base, an arrow at the tip.
            float r = 0.035f * scale;
            Handles.DrawLine(b, tip, thick);
            Handles.DrawWireDisc(b, f, r, thick);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(f), r * 1.2f, EventType.Repaint);
            if (selected)
            {
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(tip + f * (r * 1.5f), $"{length:0.###} m", label);
            }
        }
    }
}
#endif
