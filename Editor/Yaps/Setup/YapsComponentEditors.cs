// Inspectors and scene gizmos for YapsSocket and YapsPlug, built with
// UI Toolkit on the same elements as the windows. A socket's gizmo is at
// least 5 cm and grows with the view; a plug's is drawn at its baked length.
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

        // A bound control for one field, with the system tags it came from to its right. The
        // [Tooltip] stays on hover; printed under every control it buried the knobs.
        //
        // Built here rather than through BridgeElements.Bound: that hands a [Range] to
        // PropertyField, and these sliders must keep their number box. So the classes are added by
        // hand, the one place in this file that does.
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
                    // Bound by path; the inspector binds its root once it is built.
                    control = BridgeElements.Bound(p.serializedObject, p.propertyPath, label, help);
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
            control.AddToClassList("ab-grow");

            var line = new List<VisualElement> { control };
            if (!string.IsNullOrEmpty(from))
            {
                foreach (var s in from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    line.Add(BridgeElements.Tag(s, SystemSeries(s), SystemName(s)));
                }
            }
            return BridgeElements.Row(line.ToArray());
        }

        // The four systems as tag series: DPS purple, TPS teal, SPS orange, YAPS green, as the
        // README says. The material panel colours its tags from the same series.
        public static int SystemSeries(string system)
        {
            switch (system)
            {
                case "DPS": return 1;
                case "TPS": return 2;
                case "SPS": return 3;
                default: return 4;
            }
        }

        // One shape or clip stage: a stripe beside its rows, like a report row. No kit helper
        // builds one, so its classes are added here. Returns the column to fill.
        public static VisualElement Stage(VisualElement into)
        {
            var stage = new VisualElement();
            stage.AddToClassList("ab-stage");
            var stripe = new VisualElement();
            stripe.AddToClassList("ab-report-stripe");
            stripe.AddToClassList("ab-stripe-info");
            stage.Add(stripe);
            var column = new VisualElement();
            column.AddToClassList("ab-grow");
            stage.Add(column);
            into.Add(stage);
            return column;
        }

        // A depth range. No kit helper builds a MinMaxSlider; with no label it sits under the
        // control that already names the stage.
        public static MinMaxSlider Range(string label, float from, float to)
        {
            var range = label == null ? new MinMaxSlider(from, to, 0f, 1f) : new MinMaxSlider(label, from, to, 0f, 1f);
            range.AddToClassList("ab-field");
            return range;
        }

        public static string SystemName(string system)
        {
            switch (system)
            {
                case "DPS": return "From Raliv's Dynamic Penetration System";
                case "TPS": return "From Thry's Penetration System";
                case "SPS": return "From VRCFury's Super Plug Shader";
                default: return "YAPS's own: none of the others had it";
            }
        }

        // The material properties an animation may drive, by the name the
        // Animation window wants after "Material.".
        //
        // Why this component has no enable checkbox, and what to animate
        // instead. The enabled field is serialised on every Behaviour whether
        // or not it means anything, and this one does not, so animating it off
        // a slider does nothing at all.
        public const string InertComponentNote =
            "Setup data, stripped at upload: animating its Enabled field does nothing.";

        public const string PlugSwitchNote =
            "To switch the bend, animate the material's _YAPS_Enabled (0 to 1). To hide the plug, toggle its object.";

        public const string SocketSwitchNote =
            "To switch the socket off, toggle its object. _YAPS_SocketPower only scales its shapes.";

        public const string AnimatablePlugProperties =
            "_YAPS_Enabled  (deform on, 0 or 1)\n" +
            "_YAPS_Curvature, _YAPS_ReCurvature, _YAPS_EntranceStiffness  (shape at rest)\n" +
            "_YAPS_Squeeze, _YAPS_SqueezeDistance, _YAPS_Bulge, _YAPS_BulgeDistance, _YAPS_BulgeFalloff  (inside a socket)\n" +
            "_YAPS_IdleLength, _YAPS_IdleWidth, _YAPS_WriggleStrength, _YAPS_WriggleSpeed  (out of a socket)\n" +
            "_YAPS_PumpStrength, _YAPS_PumpSpeed, _YAPS_PumpWidth  (motion inside a socket)\n" +
            "_YAPS_BezierSmoothness, _YAPS_BezierStart, _YAPS_SmoothStart, _YAPS_MinimumSocketDistance  (the bend)\n" +
            "_YAPS_TaperStart, _YAPS_TaperEnd, _YAPS_Overrun  (past the opening)\n" +
            "_YAPS_BakeScale, _YAPS_BakeGirth  (the plug's size, wired at Bake from bone scale)\n" +
            "_YAPS_ShapeWeights .. _YAPS_ShapeWeights4  (its baked blendshapes, wired at Bake)";

        public const string AnimatableSocketProperties =
            "_YAPS_SocketPower  (shape strength, 0 is off)\n" +
            "_YAPS_SocketShapeStart .. _YAPS_SocketShapeStart4, _YAPS_SocketShapeFade .. _YAPS_SocketShapeFade4  (sixteen shapes, four per float4, x y z w)";
    }

    // --- preview -----------------------------------------------------------

    // Turns a socket's preview on and off for the inspector and the window.
    // Spawns a test plug when the scene has no baked plug. Repaints the
    // scene view while a preview is on or a plug is selected.
    static class YapsPreview
    {
        public const string PlugName = "YAPS Preview Plug";

        // How close a baked plug has to be before the socket counts it as
        // the one to watch. Avatar scale: everything on one body is within
        // a couple of metres of everything else, so this is arm's reach.
        public const float NearEnough = 0.4f;

        static bool _fromPrefab;
        static bool _animating;
        // When Tick last searched the scene; NegativeInfinity searches on
        // the next tick.
        static double _searched = double.NegativeInfinity;
        static YapsSocket[] _sockets = new YapsSocket[0];

        // A test plug in front of this socket, asked for outright.
        public static void DropTestPlug(YapsSocket socket)
        {
            if (socket == null) return;
            Remove();
            Spawn(socket);
            _searched = double.NegativeInfinity;
            Animate(true);
            SceneView.RepaintAll();
        }

        public const string SocketName = "YAPS Preview Socket";

        // The mirror of DropTestPlug, for the other end.
        //
        // Preview has always been socket-driven: a socket drops a plug and
        // watches it bend in. A plug had nothing to bend TOWARD, so its own
        // inspector could only ever show it straight, which reads as broken.
        //
        // The socket is built by the same code the universal prefabs are, and
        // is placed one plug length along the plug's own measured forward, the
        // position the plug is actually reaching for.
        public static void DropTestSocket(YapsPlug plug, YapsSocket.SocketKind kind = YapsSocket.SocketKind.Hole)
        {
            if (plug == null) return;
            RemoveTestSocket();
            if (!PlugFrame(plug, out var origin, out var forward, out var up, out float length))
            {
                EditorUtility.DisplayDialog("YAPS preview",
                    "This plug has not been baked yet, so there is nothing to bend. Bake it from " +
                    "Tools ▸ YAPS ▸ Setup, then preview.", "OK");
                return;
            }
            // WITH lights, like a real socket. It was built without them so a light
            // could not answer in place of the contact channel while the channel
            // was being debugged. That made the preview show the fallback route
            // instead of the one a user gets, so a plug looked worse in the editor
            // than it does in game. Turn its lights off by hand to isolate the
            // channel.
            var go = YapsSocketBuilder.BuildPreviewSocket(SocketName, kind);
            if (go == null) return;
            go.transform.SetPositionAndRotation(origin + forward * length, Quaternion.LookRotation(-forward, up));
            Undo.RegisterCreatedObjectUndo(go, "YAPS preview socket");
            EditorGUIUtility.PingObject(go);
            // Switching preview ON is what makes it do anything: the socket's own
            // PreviewTick writes _YAPS_SocketPos/Forward/Up into every plug
            // material near it, and Tick only calls it for a socket whose preview
            // is set. Placing the socket and leaving that off gives a socket that
            // sits there while the plug stays straight.
            //
            // spawnPlugIfNone: false, the plug being looked at IS the plug.
            var socket = go.GetComponent<YapsSocket>();
            if (socket != null) Set(socket, true, spawnPlugIfNone: false);
            SceneView.RepaintAll();
        }

        public static bool TestSocketInScene => GameObject.Find(SocketName) != null;

        public static void RemoveTestSocket()
        {
            var existing = GameObject.Find(SocketName);
            if (existing != null)
            {
                // Preview off BEFORE it is destroyed: that is what writes the
                // cleared flags back into every plug material this socket had
                // engaged. Destroying it first leaves the plug bent toward a
                // socket that is no longer there.
                var socket = existing.GetComponent<YapsSocket>();
                if (socket != null) Set(socket, false, spawnPlugIfNone: false);
                // Set destroys the preview socket itself, so by here it is usually
                // gone. Handing Unity a destroyed object throws ArgumentNullException
                // on objectToUndo. The null check is Unity's overloaded ==, which
                // reports a destroyed object as null, which is what is wanted.
                if (existing != null) Undo.DestroyObjectImmediate(existing);
            }
            SceneView.RepaintAll();
        }

        public static bool TestPlugInScene => GameObject.Find(PlugName) != null;

        public static void RemoveTestPlug()
        {
            Remove();
            SceneView.RepaintAll();
        }

        public static void Set(YapsSocket socket, bool on, bool spawnPlugIfNone = true)
        {
            if (socket == null) return;
            socket.preview = on;
            // A plug on the far side of the scene is no use to look at, so
            // the test one arrives unless a real plug is already close.
            if (on && spawnPlugIfNone && CountBakedPlugsNear(socket, NearEnough) == 0) Spawn(socket);
            if (!on) { Remove(); YapsShapeSim.Release(socket); }
            socket.PreviewTick();
            _searched = double.NegativeInfinity;
            Animate(on);
            // A socket the toolkit dropped exists only to be previewed against, so
            // switching its preview off is asking for it to go rather than
            // to sit there inert. Last, because PreviewTick above still
            // needs it alive to write the cleared flags back.
            if (!on && socket.gameObject.name == SocketName)
            {
                Undo.DestroyObjectImmediate(socket.gameObject);
            }
            SceneView.RepaintAll();
        }

        public static void Animate(bool on)
        {
            if (on == _animating) return;
            _animating = on;
            _searched = double.NegativeInfinity;
            EditorApplication.update -= Tick;
            if (on) EditorApplication.update += Tick;
        }

        static void Tick()
        {
            // Update fires every editor frame, too often to search the scene
            // and work out routes each time. Both are done again four times a
            // second and whenever a preview starts; the plugs' frames are
            // still read every tick.
            double now = EditorApplication.timeSinceStartup;
            if (now - _searched > 0.25)
            {
                _searched = now;
                _sockets = Object.FindObjectsOfType<YapsSocket>(true);
                YapsShapeSim.Forget();
            }
            // Stop once nothing needs it.
            bool previewing = _sockets.Any(s => s != null && s.preview);
            bool plugSelected = Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<YapsPlug>() != null;
            if (!previewing && !plugSelected) { Animate(false); return; }
            // The plug's tip drives the previewing socket's shapes.
            foreach (var s in _sockets)
                if (s != null && s.preview) YapsShapeSim.FollowPlugs(s);
            SceneView.RepaintAll();
        }

        // Baked plugs whose base is within `metres` of the socket.
        public static int CountBakedPlugsNear(YapsSocket socket, float metres)
        {
            if (socket == null) return CountBakedPlugs();
            int n = 0;
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>(true))
            {
                if (!PlugFrame(plug, out var origin, out var forward, out _, out float length)) continue;
                float gap = Mathf.Min(Vector3.Distance(origin, socket.transform.position),
                                      Vector3.Distance(origin + forward * length, socket.transform.position));
                if (gap <= metres + length) n++;
            }
            return n;
        }

        public static int CountBakedPlugs()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>(true))
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
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>(true))
                if (PlugFrame(plug, out origin, out forward, out up, out length)) return true;
            return false;
        }

        // One baked plug as a frame; false when it has no live bake.
        public static bool PlugFrame(YapsPlug plug, out Vector3 origin, out Vector3 forward, out Vector3 up, out float length)
        {
            origin = Vector3.zero; forward = Vector3.forward; up = Vector3.up; length = 0.25f;
            var r = plug != null ? plug.Target : null;
            if (r == null) return false;
            // This plug's bake: two plugs on one mesh took the first one's length.
            var mat = YapsPlugEditor.BakedMaterials(plug, r).FirstOrDefault(m =>
                m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") > 0);
            if (mat == null) return false;
            var markers = plug.transform.Find("YAPS Markers");
            var frame = markers ?? plug.transform;
            origin = frame.position; forward = frame.forward; up = frame.up;
            // The frame is in the scene, so the length has to be in metres.
            length = Mathf.Max(YapsNativeBuilder.WorldLength(r, mat), 0.05f);
            if (markers == null && r is SkinnedMeshRenderer && plug.rootBone != null)
            {
                origin = plug.rootBone.position; forward = plug.rootBone.forward; up = plug.rootBone.up;
            }
            return true;
        }

        // A test plug in front of the socket, engaged but not swallowed.
        static void Spawn(YapsSocket socket)
        {
            if (GameObject.Find(PlugName) != null) return;
            // For shapes on another mesh the game measures depth in reaches,
            // so the test plug is one reach long: all the way in reads 1.
            bool contact = socket.renderer != null && socket.shapes.Count > 0
                           && YapsNativeBuilder.ShapesByContact(socket);
            float length = contact ? YapsSocketReactions.ReachOf(socket) : 0.25f;

            // The prop prefab when there is one: it is baked on the shader
            // the project has now, which a plug built here may not be.
            _fromPrefab = false;
            GameObject go = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YapsSocketBuilder.PlugPropPrefabPath);
            if (prefab != null)
            {
                go = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                if (go != null)
                {
                    _fromPrefab = true;
                    Undo.RegisterCreatedObjectUndo(go, "YAPS preview plug");
                }
            }
            // Keep the socket selected; its inspector holds the stop button.
            if (go == null) go = YapsNativeBuilder.BuildTestPlug(null, select: false, length: length);
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
                    "reason on a [YAPS] line: usually the shader could not be patched.", "OK");
            }
        }

        // The preview plug's mesh, material and bake go with it. The shader copy stays.
        static void Remove()
        {
            var existing = GameObject.Find(PlugName);
            if (existing == null) return;
            // A prefab instance owns none of its assets: destroying them
            // would gut the prefab.
            if (_fromPrefab || PrefabUtility.IsPartOfPrefabInstance(existing))
            {
                Object.DestroyImmediate(existing);
                _fromPrefab = false;
                return;
            }
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

    // Moves a socket's shapes from a depth, in the editor, by the same
    // maths the built reactions use. Every weight goes back after.
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
            // A save would keep the shapes open, and AuthoredWeight would read
            // them back as the author's. The CCK saves the scene before it
            // saves the upload's prefab, so this covers the upload too.
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += (scene, path) => ReleaseAll();
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

        // The deepest plug's tip sets the depth: plug lengths for the
        // socket's own mesh, reaches for any other. No plug, no change.
        public static void FollowPlugs(YapsSocket socket)
        {
            if (socket == null || socket.renderer == null || socket.shapes.Count == 0) return;
            float depth = DepthFromPlugs(socket);
            if (depth < 0f) { Release(socket); return; }
            Apply(socket, depth);
        }

        // The plug list and each socket's route and trigger box, kept until
        // the preview's next scene search: working them out copies every
        // controller's layers and every renderer's materials on the avatar.
        static YapsPlug[] _plugs;
        static readonly Dictionary<int, (bool own, Vector3 offset, Vector3 size)> _routes =
            new Dictionary<int, (bool own, Vector3 offset, Vector3 size)>();

        public static void Forget()
        {
            _plugs = null;
            _routes.Clear();
        }

        public static float DepthFromPlugs(YapsSocket socket)
        {
            if (_plugs == null) _plugs = Object.FindObjectsOfType<YapsPlug>(true);
            if (!_routes.TryGetValue(socket.GetInstanceID(), out var route))
            {
                route.own = !YapsNativeBuilder.ShapesByContact(socket);
                YapsSocketReactions.TriggerBox(socket, out route.offset, out route.size);
                _routes[socket.GetInstanceID()] = route;
            }
            var (own, offset, size) = route;
            float best = -1f;
            var at = socket.transform;
            foreach (var plug in _plugs)
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
        IVisualElementScheduledItem _animationRefresh;

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

        // The same for the animations layer. A cleared list after a build
        // still rebuilds, which takes the layer out.
        void AnimationsChanged(YapsSocket socket)
        {
            _animationRefresh?.Pause();
            if (!YapsSocketReactions.AnimationsExist(socket)) return;
            _animationRefresh = _root.schedule.Execute(() =>
            {
                if (socket != null) YapsSocketReactions.BuildAnimations(socket);
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
            if (YapsShapeSim.Holding(socket)) YapsShapeSim.Apply(socket, YapsShapeSim.DepthOf(socket));
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = BridgeElements.Root(new VisualElement(), inspector: true);
            Rebuild();
            return _root;
        }

        // Callbacks ask for a rebuild from inside the elements about to go.
        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        // The last build this inspector ran and where it ran it, kept across the rebuild that
        // follows, which clears everything. The Console alone was missed.
        string _saidAt, _said, _saidMore;
        Tone _saidTone;

        void Built(string at, List<string> lines)
        {
            foreach (var line in lines) Debug.Log("[YAPS] " + line);
            _saidAt = at;
            _saidTone = lines.Any(l => l.StartsWith("✗")) ? Tone.Bad : lines.Any(l => l.Contains("⚠")) ? Tone.Warn : Tone.Good;
            _said = lines.Count == 0
                ? "Built. Nothing needed changing."
                : $"Built, with {lines.Count} note{(lines.Count == 1 ? "" : "s")}. The same lines are in the Console.";
            _saidMore = lines.Count == 0 ? null : string.Join("\n", lines);
        }

        void AddSaid(VisualElement into, string at)
        {
            if (_saidAt == at) into.Add(BridgeElements.Notice(_saidTone, _said, _saidMore));
        }

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

            _root.Add(BridgeElements.Banner((hole ? "Hole" : socket.oneWay ? "One-way ring" : "Ring") + " · " + YapsToggles.LabelFor(socket),
                built ? "readable by DPS, TPS, SPS and YAPS plugs" : "not built: no plug can find it yet",
                built ? "built" : "not built", BridgeTheme.Span.Cvr));

            // The inset the bleeding root gave up, back under the banner. A plain container, not
            // Scroll(): a scroller inside the Inspector's own would trap the wheel.
            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);
            body.Add(BridgeElements.Notice(Tone.Info,
                YapsInspectorStyle.InertComponentNote + " " + YapsInspectorStyle.SocketSwitchNote));

            // BUILT BY AN OLDER TOOLKIT?
            //
            // A prop is built once and never revisited, so every fix ships to new
            // ones and reaches no existing one, and the two look identical from the
            // outside. A socket carrying half-size trigger boxes, or a channel that
            // configured one material out of three, gives no sign of it. The stamp
            // is written when the socket is baked or its prop is built.
            if (!string.IsNullOrEmpty(socket.builtBy) && socket.builtBy != BridgeDefines.Version)
            {
                body.Add(BridgeElements.Notice(Tone.Warn,
                    "This socket is behind the toolkit: built by " + socket.builtBy + ", this is " + BridgeDefines.Version +
                    ". Rebuild for the fixes since: the prop builder on a prop, Bake every plug and verify on an avatar."));
            }

            // Nothing happens in game until a build has run: markers for plugs
            // to find, shapes for the mesh, a layer for the clips. One notice
            // says which is missing, and holds the only Build this socket.
            var current = rendererProp.objectReferenceValue as SkinnedMeshRenderer;
            bool contactRoute = current != null && YapsNativeBuilder.ShapesByContact(socket);
            string unbuilt = !built ? "Not built yet: no plug can find it."
                : current != null && shapesProp.arraySize > 0
                  && !(contactRoute ? YapsSocketReactions.Exists(socket) : FindSocketMaterial(socket) != null)
                    ? "Not built yet: its shapes do nothing in game."
                : socket.depthAnimations.Any(a => a != null && a.clip != null) && !YapsSocketReactions.AnimationsExist(socket)
                    ? "Not built yet: its animations play nothing in game."
                : null;
            if (unbuilt != null)
            {
                body.Add(BridgeElements.Notice(Tone.Warn,
                    unbuilt + " Before upload, Bake every plug and verify in YAPS Setup builds every socket.",
                    action: BridgeElements.Btn("Build this socket", () =>
                    {
                        Built("build", YapsNativeBuilder.BuildSocket(socket));
                        RebuildLater();
                    }, "Builds this socket alone: markers, shapes, clips and its menu toggle.", ButtonKind.Strong)));
            }
            AddSaid(body, "build");

            // What it is.
            var what = new BridgeElements.Card("What it is");
            what.Body.Add(BridgeElements.Choice("Kind",
                "A hole closes around the plug and stops it: a mouth, a pussy, an anus. " +
                "A ring lets the plug pass straight through: a hand, thighs, a foot.",
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
            if (!hole)
            {
                // The kind rides the atlas writer's material, so the writer
                // is rebuilt when it changes, as it is for hole and ring.
                var oneWayProp = so.FindProperty("oneWay");
                var oneWay = YapsInspectorStyle.Field(oneWayProp, type.GetField("oneWay"), "One way");
                oneWay.TrackPropertyValue(oneWayProp, p =>
                {
                    if (!built) return;
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "YAPS one-way ring");
                    YapsSocketBuilder.Build(socket);
                    RebuildLater();
                });
                what.Body.Add(oneWay);
            }

            what.Body.Add(YapsInspectorStyle.Field(so.FindProperty("tags"),
                typeof(YapsSocket).GetField("tags"), "Tags"));
            body.Add(what);

            // Shapes.
            var opens = new BridgeElements.Card("Opens as a plug goes in");
            opens.Body.Add(BridgeElements.Hint(
                "A mesh, usually the body, and up to sixteen of its shapes, each over its own depth range."));

            var renderers = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList()
                : new List<SkinnedMeshRenderer>();
            // The user's choice, never a guess: a guess written on every
            // draw made "None" impossible and dirtied a socket on selection.
            if (current != null && !renderers.Contains(current)) renderers.Insert(0, current);
            var rNames = new List<string> { "None: bend plugs, play no shape" };
            rNames.AddRange(renderers.Select(r => $"{r.name} · {r.sharedMesh.blendShapeCount} shapes"));
            int rIndex = current != null ? renderers.IndexOf(current) : -1;
            opens.Body.Add(BridgeElements.Popup("Mesh", "The mesh whose shapes open as a plug goes in.",
                rNames.ToArray(), rIndex + 1, index =>
                {
                    int picked = index - 1;
                    YapsShapeSim.Release(socket);
                    rendererProp.objectReferenceValue = picked >= 0 && picked < renderers.Count ? renderers[picked] : null;
                    so.ApplyModifiedProperties();
                    ReactionsChanged(socket);
                    RebuildLater();
                }));
            if (contactRoute)
            {
                float reach = YapsSocketReactions.ReachOf(socket);
                string reachFrom = socket.depthReach > 0f ? "set below"
                    : YapsSocketReactions.LongestPlugOn(avatarRoot) > 0f ? "the length of the longest plug on this avatar"
                    : "the default, no plug on this avatar yet";
                opens.Body.Add(BridgeElements.Notice(Tone.Info,
                    "Opens through a contact and an animator layer, on a synced depth (32 of 3200 sync bits). " +
                    $"DPS plugs have no pointer and do not open it. Full depth is {reach:0.00} m in ({reachFrom})."));
            }

            if (current != null)
            {
                var mesh = current.sharedMesh;
                var shapeNames = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
                var options = new List<string> { "pick a shape" };
                options.AddRange(shapeNames);
                var choices = options.ToArray();
                // Rows are named by their depth; several rows may share one.
                string StageName(int i, float at) => i == 0 && at <= 0.001f ? "Entry" : $"At {at:0.00}";

                for (int i = 0; i < shapesProp.arraySize && i < YapsBaker.MaxShapes; i++)
                {
                    int index = i;
                    var row = shapesProp.GetArrayElementAtIndex(i);
                    var name = row.FindPropertyRelative("blendshape");
                    var start = row.FindPropertyRelative("startsAt");
                    var fade = row.FindPropertyRelative("fadeOver");
                    int sIndex = shapeNames.IndexOf(name.stringValue);

                    var stage = YapsInspectorStyle.Stage(opens.Body);
                    var shapePopup = BridgeElements.Popup(StageName(i, start.floatValue),
                        "The shape that opens over this depth range.", choices, sIndex + 1, pickedIndex =>
                        {
                            int picked = pickedIndex - 1;
                            // The old shape goes back before the new one moves.
                            YapsShapeSim.Release(socket);
                            name.stringValue = picked >= 0 ? shapeNames[picked] : "";
                            so.ApplyModifiedProperties();
                            ReactionsChanged(socket);
                            Retest(socket);
                        });
                    shapePopup.AddToClassList("ab-grow");
                    stage.Add(BridgeElements.Row(shapePopup, BridgeElements.Btn("Remove", () =>
                    {
                        YapsShapeSim.Release(socket);
                        shapesProp.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        ReactionsChanged(socket);
                        RebuildLater();
                    }, "Takes this shape out.", ButtonKind.Danger)));

                    // Depth as one range bar.
                    float s0 = start.floatValue, e0 = Mathf.Min(1f, start.floatValue + fade.floatValue);
                    var range = YapsInspectorStyle.Range(null, s0, e0);
                    var readout = BridgeElements.Hint($"{s0:0.00} → {e0:0.00} of the plug's length");
                    range.RegisterValueChangedCallback(e =>
                    {
                        start.floatValue = e.newValue.x;
                        fade.floatValue = Mathf.Max(0.01f, e.newValue.y - e.newValue.x);
                        so.ApplyModifiedProperties();
                        readout.text = $"{e.newValue.x:0.00} → {e.newValue.y:0.00} of the plug's length";
                        ReactionsChanged(socket);
                        Retest(socket);
                    });
                    stage.Add(range);
                    stage.Add(readout);
                    if (!string.IsNullOrEmpty(name.stringValue) && sIndex < 0)
                        stage.Add(BridgeElements.Notice(Tone.Warn, $"\"{name.stringValue}\" is not on this mesh."));
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
                    opens.Body.Add(n == 0
                        ? BridgeElements.ButtonRow(BridgeElements.TextLink("+ Add the entry shape", () => AddRow(0f, 0.3f)))
                        : BridgeElements.ButtonRow(
                            BridgeElements.TextLink("+ Another shape at the same depth", () => AddRow(lastStart, lastFade)),
                            BridgeElements.TextLink("+ A shape deeper in", () => AddRow(Mathf.Min(1f, lastStart + 0.25f), 0.3f))));
                    opens.Body.Add(BridgeElements.Hint($"{n} of {YapsBaker.MaxShapes} shapes."));
                }
                if (shapesProp.arraySize > 0)
                {
                    var strength = YapsInspectorStyle.Field(powerProp, type.GetField("shapePower"), "Strength");
                    // Written through as it moves: the material once baked,
                    // the reactions layer once built, the test on the mesh.
                    strength.TrackPropertyValue(powerProp, p =>
                    {
                        PushStrength(socket, FindSocketMaterial(socket));
                        ReactionsChanged(socket);
                    });
                    opens.Body.Add(strength);
                    if (contactRoute)
                    {
                        var reachProp = so.FindProperty("depthReach");
                        var reachField = YapsInspectorStyle.Field(reachProp, type.GetField("depthReach"), "Full depth (m)");
                        reachField.TrackPropertyValue(reachProp, p => { ReactionsChanged(socket); Retest(socket); RebuildLater(); });
                        opens.Body.Add(reachField);
                    }
                }
            }
            else if (renderers.Count == 0)
            {
                opens.Body.Add(BridgeElements.Notice(Tone.Info, avatarRoot == null
                    ? "Put this socket under an avatar or prop and its meshes will appear here."
                    : "No skinned mesh with blendshapes under this avatar: the socket bends plugs and plays no shape."));
            }
            body.Add(opens);

            // The author's clips, played by the same depth as the shapes.
            var animsProp = so.FindProperty("depthAnimations");
            var plays = new BridgeElements.Card("Plays as a plug goes in");
            plays.Body.Add(BridgeElements.Hint(
                $"Your own clips, each blended in over its own depth range. Full depth is {YapsSocketReactions.ReachOf(socket):0.00} m in.",
                "Write defaults are on in their layer, so animate only what nothing else on the avatar animates."));
            for (int i = 0; i < animsProp.arraySize; i++)
            {
                int index = i;
                var row = animsProp.GetArrayElementAtIndex(i);
                var clipProp = row.FindPropertyRelative("clip");
                var start = row.FindPropertyRelative("startsAt");
                var fade = row.FindPropertyRelative("fadeOver");

                var stage = YapsInspectorStyle.Stage(plays.Body);
                var clipField = BridgeElements.ObjectPicker<AnimationClip>("Animation", clipProp.objectReferenceValue as AnimationClip, clip =>
                {
                    clipProp.objectReferenceValue = clip;
                    so.ApplyModifiedProperties();
                    AnimationsChanged(socket);
                    // Shows or clears the not-built note.
                    RebuildLater();
                }, "A clip blended in over this depth range.", sceneObjects: false);
                clipField.AddToClassList("ab-grow");
                stage.Add(BridgeElements.Row(clipField, BridgeElements.Btn("Remove", () =>
                {
                    animsProp.DeleteArrayElementAtIndex(index);
                    so.ApplyModifiedProperties();
                    AnimationsChanged(socket);
                    RebuildLater();
                }, "Takes this clip out.", ButtonKind.Danger)));

                float s0 = start.floatValue, e0 = Mathf.Min(1f, start.floatValue + fade.floatValue);
                var range = YapsInspectorStyle.Range(null, s0, e0);
                var readout = BridgeElements.Hint($"{s0:0.00} → {e0:0.00} of full depth");
                range.RegisterValueChangedCallback(e =>
                {
                    start.floatValue = e.newValue.x;
                    fade.floatValue = Mathf.Max(0.01f, e.newValue.y - e.newValue.x);
                    so.ApplyModifiedProperties();
                    readout.text = $"{e.newValue.x:0.00} → {e.newValue.y:0.00} of full depth";
                    AnimationsChanged(socket);
                });
                stage.Add(range);
                stage.Add(readout);
            }
            // The shapes card shows this only for the contact route, and
            // animations always take it.
            if (animsProp.arraySize > 0 && !(contactRoute && shapesProp.arraySize > 0))
            {
                var reachProp = so.FindProperty("depthReach");
                var reachField = YapsInspectorStyle.Field(reachProp, type.GetField("depthReach"), "Full depth (m)");
                reachField.TrackPropertyValue(reachProp, p => { AnimationsChanged(socket); RebuildLater(); });
                plays.Body.Add(reachField);
            }
            plays.Body.Add(BridgeElements.ButtonRow(BridgeElements.TextLink("+ Add an animation", () =>
            {
                animsProp.arraySize++;
                var row = animsProp.GetArrayElementAtIndex(animsProp.arraySize - 1);
                row.FindPropertyRelative("clip").objectReferenceValue = null;
                row.FindPropertyRelative("startsAt").floatValue = 0f;
                row.FindPropertyRelative("fadeOver").floatValue = 0.3f;
                so.ApplyModifiedProperties();
                RebuildLater();
            })));
            body.Add(plays);

            // See it work.
            int bakedPlugs = YapsPreview.CountBakedPlugsNear(socket, YapsPreview.NearEnough);
            var see = new BridgeElements.Card("See it work");
            see.Body.Add(BridgeElements.Hint(
                "Bends every baked plug in the scene toward this socket, editor only." +
                (bakedPlugs > 0 ? "" : " With no plug near, it drops a test one."),
                contactRoute && shapesProp.arraySize > 0
                    ? $"Shapes follow the tip: 0 at the opening, 1 at {YapsSocketReactions.ReachOf(socket):0.00} m in."
                    : null));
            // Pressed while it runs. The label stays the one the README quotes.
            var previewButton = BridgeElements.Btn("Preview", () => { YapsPreview.Set(socket, !socket.preview); RebuildLater(); },
                socket.preview ? "Previewing. Click again to stop."
                : bakedPlugs > 0 ? "Bends the baked plugs near it toward this socket."
                : "No baked plug is near, so this drops a test one in front of it.",
                ButtonKind.Strong);
            previewButton.EnableInClassList("ab-on", socket.preview);
            // Asked for outright, since guessing whether the avatar's own
            // plug is the one to watch gets it wrong on a body where
            // everything is within arm's reach of everything.
            bool testPlug = YapsPreview.TestPlugInScene;
            see.Body.Add(BridgeElements.ButtonRow(previewButton, BridgeElements.Btn(
                testPlug ? "Take the test plug away" : "Drop a test plug here",
                () =>
                {
                    if (testPlug) YapsPreview.RemoveTestPlug();
                    else { YapsPreview.Set(socket, true, spawnPlugIfNone: false); YapsPreview.DropTestPlug(socket); }
                    RebuildLater();
                },
                testPlug ? "Takes the test plug out of the scene." : "Drops a test plug in front of it, even with a baked one near.")));

            // The plug mesh on most avatars only exists in Play Mode: it ships
            // switched off and a toggle brings it in. So the one state where you
            // can SEE a plug was the one state the preview refused to run in, and
            // every editor bend came from a marker light instead.
            see.Body.Add(BridgeElements.Bind("Keep previewing in Play Mode",
                "For a plug that only appears in Play Mode, or to see the bend on a posed avatar.",
                socket.previewInPlayMode, on =>
                {
                    Undo.RecordObject(socket, "YAPS preview in play mode");
                    socket.previewInPlayMode = on;
                    EditorUtility.SetDirty(socket);
                }));

            // Applied at once on a built socket, menu included, like the kind.
            see.Body.Add(BridgeElements.Bind("In-game readout",
                "Hidden until the avatar's YAPS readout menu toggle shows it. Once nothing on the avatar " +
                "carries a readout, the menu toggle and its synced bit go too.",
                socket.readout, on =>
                {
                    Undo.RecordObject(socket, "YAPS socket readout");
                    socket.readout = on;
                    EditorUtility.SetDirty(socket);
                    if (built) Built("readout", YapsNativeBuilder.BuildSocket(socket));
                    RebuildLater();
                }));
            AddSaid(see.Body, "readout");

            // The shapes, tried here: a depth slider moves them on the mesh
            // in the editor; while previewing, the plug's tip is the depth.
            if (current != null && shapesProp.arraySize > 0)
            {
                int id = socket.GetInstanceID();
                var test = BridgeElements.SliderField("Test depth", "0 at the opening, 1 at full depth.",
                    0f, 1f, YapsShapeSim.Slider.TryGetValue(id, out float held) ? held : 0f, depth =>
                    {
                        // Previewing, the plug's tip is the depth and the slider only shows it.
                        if (socket.preview) return;
                        YapsShapeSim.Slider[id] = depth;
                        YapsShapeSim.Apply(socket, depth);
                        SceneView.RepaintAll();
                    });
                if (socket.preview)
                {
                    test.SetEnabled(false);
                    test.schedule.Execute(() => test.SetValueWithoutNotify(YapsShapeSim.DepthOf(socket))).Every(100);
                }
                see.Body.Add(test);
                see.Body.Add(BridgeElements.Hint(socket.preview
                    ? "The preview plug drives the shapes. Nothing is saved."
                    : "Moves the shapes in the editor. Nothing is saved; they reset when you click away."));
            }
            body.Add(see);

            // The socket-side knobs live on the material. Drawn here too once baked.
            var bakedMat = FindSocketMaterial(socket);
            if (bakedMat != null)
            {
                var opensHow = new BridgeElements.Card("How it opens");
                opensHow.Body.Add(BridgeElements.Hint(
                    "Baked ranges, as fractions of the plug's length. Edits the material directly; the next build overwrites them."));
                // One range per baked shape, named after the shape when the
                // component knows it, else by its slot. Named from the rows the
                // bake kept: it skips shapes the mesh lacks and repeats, so row i
                // is not always baked shape i.
                int baked = bakedMat.HasProperty("_YAPS_ShapeCount") ? Mathf.RoundToInt(bakedMat.GetFloat("_YAPS_ShapeCount")) : 0;
                var shapeMesh = socket.renderer.sharedMesh;
                var kept = socket.shapes
                    .Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)
                                && shapeMesh != null && shapeMesh.GetBlendShapeIndex(s.blendshape) >= 0)
                    .Select(s => s.blendshape).Distinct().Take(YapsBaker.MaxShapes).ToList();
                for (int i = 0; i < Mathf.Min(baked, YapsBaker.MaxShapes); i++)
                {
                    int stage = i;
                    var (st0, fd0) = YapsNativeBuilder.ReadStage(bakedMat, i);
                    string label = i < kept.Count ? kept[i] : $"Shape {i}";
                    var mm = YapsInspectorStyle.Range(label, st0, Mathf.Min(1f, st0 + fd0));
                    mm.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(bakedMat, "YAPS socket shape");
                        YapsNativeBuilder.WriteStage(bakedMat, stage, e.newValue.x, e.newValue.y - e.newValue.x);
                        EditorUtility.SetDirty(bakedMat);
                    });
                    opensHow.Body.Add(mm);
                }
                opensHow.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Open material", () => Selection.activeObject = bakedMat)));
                body.Add(opensHow);
            }

            // Advanced.
            // The socket-side knobs are material properties too.
            if (bakedMat != null)
            {
                var animate = new BridgeElements.Card("Animate it", null, false);
                animate.Body.Add(BridgeElements.Hint(
                    "Key these in the Animation window under Skinned Mesh Renderer ▸ Material."));
                animate.Body.Add(BridgeElements.Code(YapsInspectorStyle.AnimatableSocketProperties));
                body.Add(animate);
            }

            var advanced = new BridgeElements.Card("Advanced");
            advanced.Body.Add(YapsInspectorStyle.Field(lightsProp, type.GetField("emitLights"), "Emit marker lights"));
            // The box is ticked and the socket still has none. Say why here,
            // where it was ticked, rather than only in the build log.
            string capped = YapsSocketBuilder.LightCapNote(socket);
            if (capped != null) advanced.Body.Add(BridgeElements.Notice(Tone.Info, char.ToUpper(capped[0]) + capped.Substring(1)));
            advanced.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn(built ? "Rebuild markers" : "Build markers", () =>
                {
                    Undo.RegisterFullObjectHierarchyUndo(socket.gameObject, "Rebuild YAPS socket");
                    YapsSocketBuilder.Build(socket);
                    RebuildLater();
                }),
                BridgeElements.Btn("Open YAPS Setup", YapsSetupWindow.Open)));
            advanced.Section("Remove");
            advanced.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Remove this socket", () =>
            {
                var parent = socket.transform.parent;
                if (YapsRemover.Ask(socket)) Selection.activeTransform = parent;
            }, null, ButtonKind.Danger)));
            advanced.Body.Add(BridgeElements.Hint(
                "Removes its objects, animator layers, menu toggle and bake. One undo step."));
            body.Add(advanced);

            _root.Bind(so);
        }

        // The material with this socket's baked deform: the slot its bake
        // recorded. Matching by renderer took a plug's bake on the same mesh,
        // and with no mesh, another socket's anywhere on the avatar.
        static Material FindSocketMaterial(YapsSocket socket)
        {
            var r = socket.renderer;
            if (r == null) return null;
            var mats = r.sharedMaterials;
            if (socket.bakedRenderer == r)
            {
                int slot = socket.bakedSlot;
                return slot >= 0 && slot < mats.Length && IsSocketBake(mats[slot]) ? mats[slot] : null;
            }
            // Baked before the slot was recorded; a rebuild records it.
            return socket.bakedRenderer == null && socket.bakedFrom != null ? mats.FirstOrDefault(IsSocketBake) : null;
        }

        // A socket bake switches the plug deform off. Power alone cannot
        // tell: every patched shader declares it, and Strength can be 0.
        // The material panel asks the same question.
        internal static bool IsSocketBake(Material m) =>
            m != null && m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null
            && m.HasProperty("_YAPS_SocketPower")
            && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") <= 0f;

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

        // The avatar or prop, as every other "which avatar" answer: the first
        // Animator up stopped at an accessory carrying one of its own, and
        // the socket's shape list lost the body. Before conversion there is
        // neither, and the first Animator up is still the avatar.
        internal static Transform AvatarRootOf(Transform t)
        {
            var root = YapsNativeBuilder.AvatarRoot(t);
            if (root == null || root.GetComponent<ABI.CCK.Components.CVRAvatar>() != null
                || root.GetComponent<ABI.CCK.Components.CVRSpawnable>() != null) return root;
            for (var at = t; at != null; at = at.parent)
            {
                if (at.GetComponent<Animator>() != null) return at;
            }
            return root;
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
            // Five centimetres, the size of the thing it marks, and never smaller
            // than the view can show. A bone's lossyScale is the mesh's unit
            // conversion, not the avatar's size, so it plays no part here.
            float r = Mathf.Max(0.05f, HandleUtility.GetHandleSize(c) * 0.06f);
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
                Handles.Label(c + u * (r * 1.4f), (hole ? "hole" : "ring") + (built ? "" : ", not built"), label);
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
            "Past the opening",
        };

        // One short line in each fold's header. The knobs keep their own tooltips.
        static readonly Dictionary<string, string> FoldSummaries = new Dictionary<string, string>
        {
            { "Shape at rest", "how it sits when nothing bends it" },
            { "Inside a socket", "the grip and the swell at the opening" },
            { "Out of a socket", "shrink and wriggle while free" },
            { "Motion inside a socket", "a stroke along the shaft" },
            { "The bend toward a socket", "how the shaft arrives" },
            { "Past the opening", "taper, and carrying on through a ring" },
            { "How sockets find it", "the lights and pointers it carries" },
            { "Which sockets it answers", "tags it answers and refuses" },
            { "Your own sockets", "which of yours it may enter" },
        };

        // The filter, kept for the session.
        static readonly string[] Systems = { "All systems", "DPS", "TPS", "SPS", "YAPS" };
        static string _filter = "All systems";

        // A knob from no system always passes, so the mesh fields never empty out.
        static bool Passes(string from)
        {
            if (_filter == "All systems" || string.IsNullOrEmpty(from)) return true;
            return from.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries).Contains(_filter);
        }

        // A fold the filter emptied is hidden.
        static void FinishFold(BridgeElements.Card fold)
        {
            if (fold != null && fold.Body.childCount == 0) fold.AddToClassList("ab-hidden");
        }

        // The first fold in a card starts open and the rest closed, so the card reads as a list
        // of what is in it. Each remembers how it was left.
        static BridgeElements.Card Fold(string title, VisualElement owner)
        {
            FoldSummaries.TryGetValue(title, out string summary);
            var fold = new BridgeElements.Card(title, summary, owner.childCount == 0).Nested().Remember("Yaps.Plug." + title);
            owner.Add(fold);
            return fold;
        }

        // A selected plug animates in the scene view.
        void OnEnable() => YapsPreview.Animate(true);

        // Which of the wearer's own sockets this plug may enter, one tick
        // each. Applied on the click, to the materials alone, so there is
        // nothing to rebuild.
        static void OwnSockets(YapsPlug plug, VisualElement into)
        {
            var avatar = plug.GetComponentInParent<ABI.CCK.Components.CVRAvatar>(true);
            var own = avatar != null ? avatar.GetComponentsInChildren<YapsSocket>(true) : new YapsSocket[0];
            if (own.Length == 0) return;

            var card = Fold("Your own sockets", into);
            card.Body.Add(BridgeElements.Hint("Which of your own sockets this plug may enter.",
                "Hips and sockets its tags refuse start clear; a tick you set beats its tags. " +
                "The in-game own sockets toggle opens them all. Needs the screen atlas."));
            if (own.Length > YapsOwner.MaxSelfSockets)
                card.Body.Add(BridgeElements.Hint(
                    $"Only {YapsOwner.MaxSelfSockets} can be told apart; the rest follow the hips rule."));

            foreach (var s in own)
            {
                var socket = s;
                bool byDefault = YapsOwner.EntersByDefault(plug, socket);
                bool now = !plug.selfRefuse.Contains(socket) && (byDefault || plug.selfEnter.Contains(socket));
                card.Body.Add(BridgeElements.Bind(YapsToggles.LabelFor(socket), null, now, on =>
                {
                    Undo.RecordObject(plug, "YAPS own sockets");
                    plug.selfEnter.Remove(socket);
                    plug.selfRefuse.Remove(socket);
                    if (on != byDefault) (on ? plug.selfEnter : plug.selfRefuse).Add(socket);
                    EditorUtility.SetDirty(plug);
                    YapsOwner.ApplySelf(avatar.gameObject);
                }));
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = BridgeElements.Root(new VisualElement(), inspector: true);
            Rebuild();
            return _root;
        }

        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        // The last bake's result, kept across the rebuild that follows it.
        // The Console alone was missed.
        string _said, _saidMore;
        Tone _saidTone;

        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var plug = (YapsPlug) target;
            var renderer = plug.Target;
            var baked = BakedMaterials(plug, renderer);
            bool isBaked = baked.Count > 0;
            // In metres: the material holds a plain mesh's length in its own units.
            float len = isBaked ? YapsNativeBuilder.WorldLength(renderer, baked[0]) : 0f;

            // Named as the socket and the window's rows name it.
            _root.Add(BridgeElements.Banner("Plug · " + YapsToggles.LabelFor(plug),
                renderer == null ? "no renderer: pick the mesh that bends"
                : isBaked ? $"baked · {len:0.###} m · {baked[0].name}" : "not baked yet: set it up, then Bake",
                isBaked ? "baked" : "not baked", BridgeTheme.Span.Cvr));

            // The inset the bleeding root gave up, back under the banner. A plain container, not
            // Scroll(): a scroller inside the Inspector's own would trap the wheel.
            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);
            body.Add(BridgeElements.Notice(Tone.Info,
                YapsInspectorStyle.InertComponentNote + " " + YapsInspectorStyle.PlugSwitchNote,
                action: BridgeElements.Btn("Open YAPS Setup", YapsSetupWindow.Open)));
            if (!isBaked) body.Add(BridgeElements.Notice(Tone.Warn, "Not baked yet. Set up the mesh, then press Bake at the bottom."));

            // Every knob is tagged with its system, and the filter shows one
            // system. Above every card, since it reaches all of them.
            body.Add(BridgeElements.Popup("Show",
                "Each knob is tagged with the system it came from. Pick one to show only its knobs; " +
                "knobs from no system always show.",
                Systems, System.Array.IndexOf(Systems, _filter), i => { _filter = Systems[i]; RebuildLater(); }));

            var see = new BridgeElements.Card("See it work");
            var mesh = new BridgeElements.Card("Mesh");
            var move = new BridgeElements.Card("How it moves");
            var sockets = new BridgeElements.Card("Sockets");
            body.Add(see); body.Add(mesh); body.Add(move); body.Add(sockets);

            // The mirror of the socket's preview. A plug on its own has
            // nothing to bend toward, so its inspector could only ever show
            // it straight, which reads as a plug that does not work.
            see.Body.Add(BridgeElements.Hint("Drops a test socket ahead and bends this plug into it, editor only.",
                "Needs the plug baked and _YAPS_Enabled at 1: preview does not run your animator."));
            if (YapsPreview.TestSocketInScene)
            {
                see.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Take the test socket away", () =>
                {
                    YapsPreview.RemoveTestSocket();
                    RebuildLater();
                })));
            }
            else
            {
                // Both kinds, because they are not the same test: a hole
                // closes around the shaft and stops it, a ring slides along
                // it. A plug that looks right entering one can be wrong in
                // the other.
                var row = new List<VisualElement>();
                foreach (var kind in new[] { YapsSocket.SocketKind.Hole, YapsSocket.SocketKind.Ring })
                {
                    var k = kind;
                    bool intoHole = k == YapsSocket.SocketKind.Hole;
                    var b = BridgeElements.Btn(intoHole ? "Preview into a hole" : "Preview through a ring",
                        () => { YapsPreview.DropTestSocket(target as YapsPlug, k); RebuildLater(); },
                        !isBaked ? "Bake first"
                        : intoHole ? "A test hole ahead: it closes around the shaft and stops it."
                        : "A test ring ahead: the shaft slides through it.");
                    b.SetEnabled(isBaked);
                    row.Add(b);
                }
                see.Body.Add(BridgeElements.ButtonRow(row.ToArray()));
            }
            // A slot on the mesh and, while anything carries one, a synced bit
            // and a menu row, so it can be left off.
            see.Body.Add(BridgeElements.Bound(serializedObject, nameof(YapsPlug.readout), "In-game readout",
                "Hidden until the avatar's YAPS readout menu toggle shows it. Untick and re-bake to leave it off. " +
                "Once nothing on the avatar carries a readout, the menu toggle and its synced bit go too."));

            // Fields in declaration order; a [Header] opens a section. The
            // first fields sit under the Mesh card's own title.
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            string header = "Mesh";
            VisualElement into = mesh.Body;
            BridgeElements.Card fold = null;
            while (it.NextVisible(false))
            {
                var field = typeof(YapsPlug).GetField(it.name);
                var h = field?.GetCustomAttributes(typeof(HeaderAttribute), false).FirstOrDefault() as HeaderAttribute;
                var from = (field?.GetCustomAttributes(typeof(YapsFromAttribute), false).FirstOrDefault() as YapsFromAttribute)?.System;
                if (h != null && h.header != header)
                {
                    FinishFold(fold);
                    header = h.header;
                    if (MeshHeaders.Contains(header))
                    {
                        fold = null;
                        mesh.Section(header);
                        into = mesh.Body;
                    }
                    else
                    {
                        fold = Fold(header, MoveHeaders.Contains(header) ? move.Body : sockets.Body);
                        into = fold.Body;
                    }
                }
                if (!Passes(from)) continue;
                into.Add(YapsInspectorStyle.Field(it.Copy(), field, null, from));
            }
            FinishFold(fold);
            if (Passes("YAPS")) OwnSockets(plug, sockets.Body);

            var bake = new BridgeElements.Card("Bake");
            bake.Body.Add(BridgeElements.Hint(isBaked
                ? "Knobs write to the material and its YAPS panel writes back. Mesh, bone and measurement changes need a re-bake."
                : "Set up the mesh, then Bake: it measures the mesh and patches its shader, or uses YAPS Simple Lit."));
            bake.Body.Add(new BridgeElements.PrimaryButton(isBaked ? "Re-bake" : "Bake", () =>
            {
                var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
                string said = o.Message + (o.Notes.Count > 0 ? "  " + string.Join("  ", o.Notes) : "");
                if (o.Ok) Debug.Log("[YAPS] " + said); else Debug.LogError("[YAPS] " + said);
                _said = o.Message;
                _saidMore = o.Notes.Count > 0 ? string.Join("\n", o.Notes) : null;
                _saidTone = o.Ok ? Tone.Good : Tone.Bad;
                RebuildLater();
            }, BridgeTheme.BridgeTo));
            if (_said != null) bake.Body.Add(BridgeElements.Notice(_saidTone, _said, _saidMore));
            bake.Section("Remove");
            bake.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Remove this plug", () =>
            {
                var parent = plug.transform.parent;
                if (YapsRemover.Ask(plug)) Selection.activeTransform = parent;
            }, null, ButtonKind.Danger)));
            if (isBaked)
            {
                bake.Body.Add(BridgeElements.Hint(
                    "Remove puts the original material back and takes out the size wiring, menu toggle and markers. One undo step."));
            }
            body.Add(bake);

            // Every knob is a material property, so every knob is animatable
            // from the avatar's own animator; the names are what to record.
            var animate = new BridgeElements.Card("Animate it", null, false);
            animate.Body.Add(BridgeElements.Hint(
                "Key these in the Animation window under the renderer ▸ Material. Size sliders that scale " +
                "the bone or a shape are wired at Bake."));
            animate.Body.Add(BridgeElements.Code(YapsInspectorStyle.AnimatablePlugProperties));
            body.Add(animate);

            _root.Bind(serializedObject);

            // Knobs write through to the material. The material panel writes back.
            body.TrackSerializedObjectValue(serializedObject, so =>
            {
                var mats = BakedMaterials(plug, plug.Target);
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
                void Write(Renderer on, Material m)
                {
                    YapsNativeBuilder.WriteKnobs(plug, m);
                    if (plug.lengthOverride > 0) m.SetFloat("_YAPS_Length", YapsNativeBuilder.BakeLength(on, m, plug.lengthOverride));
                    EditorUtility.SetDirty(m);
                }
                foreach (var m in mats) Write(plug.Target, m);
                // The other meshes the bake reached, or they bend on the last
                // bake's knobs and the seam opens. Not one another plug claims.
                var top = YapsNativeBuilder.AvatarRoot(plug.transform);
                foreach (var b in plug.bakedSlots)
                {
                    if (b == null || b.renderer == null || b.renderer == plug.Target) continue;
                    var claimed = YapsNativeBuilder.OwnerPlugOf(top, b.renderer);
                    if (claimed != null && claimed != plug) continue;
                    var worn = b.renderer.sharedMaterials;
                    if (b.slot >= 0 && b.slot < worn.Length && worn[b.slot] != null && worn[b.slot].HasProperty("_YAPS_Bake"))
                        Write(b.renderer, worn[b.slot]);
                }
                // The alternate looks a toggle swaps in, or they keep the knobs of the last bake.
                YapsSwapFollow.Refresh(plug);
                if ((mats.Count > 0) != isBaked) RebuildLater();
                SceneView.RepaintAll();
            });
        }

        // This plug's baked materials on the renderer. After a slot split two
        // plugs share one, and a slot only another plug owns took this one's
        // knobs and length. A slot nobody recorded stays, as before.
        internal static List<Material> BakedMaterials(YapsPlug plug, Renderer renderer)
        {
            var list = new List<Material>();
            if (renderer == null) return list;
            var others = YapsNativeBuilder.AvatarRoot(plug.transform).GetComponentsInChildren<YapsPlug>(true).Where(p => p != plug).ToList();
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_YAPS_Bake") || !m.HasProperty("_YAPS_Length")) continue;
                if (!YapsNativeBuilder.OwnsSlot(plug, renderer, i, m)
                    && others.Any(p => YapsNativeBuilder.OwnsSlot(p, renderer, i, m))) continue;
                list.Add(m);
            }
            return list;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy | GizmoType.Pickable)]
        static void DrawPlug(YapsPlug plug, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            var renderer = plug.Target;
            var colour = YapsInspectorStyle.PlugColour; colour.a = selected ? 1f : 0.7f;
            Handles.color = colour;

            // Handles draw in world metres, so the length has to be in them
            // too. A rig whose mesh is modelled small and scaled up by its
            // bones has a large lossyScale and a perfectly ordinary plug.
            // This plug's own bake: two plugs on one mesh drew the first one's.
            var mat = BakedMaterials(plug, renderer).FirstOrDefault();
            float length = mat != null ? YapsNativeBuilder.WorldLength(renderer, mat) : 0f;

            // The markers object is the frame when built.
            var frame = plug.transform.Find("YAPS Markers") ?? plug.transform;
            Vector3 b = frame.position, f = frame.forward;
            float thick = selected ? 4f : 3f;

            if (length <= 0f)
            {
                // Nothing measured yet, so the stub is sized to the view.
                float stub = HandleUtility.GetHandleSize(b) * 0.3f;
                Handles.DrawDottedLine(b, b + f * stub, 5f);
                var label = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 11 };
                Handles.Label(b + f * stub, "plug, not baked", label);
                return;
            }
            Vector3 tip = b + f * length;
            // True length along the axis, a ring at the base, an arrow at the tip.
            float r = length * 0.04f;
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
