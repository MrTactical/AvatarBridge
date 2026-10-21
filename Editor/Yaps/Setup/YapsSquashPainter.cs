// Tools > YAPS > Squash regions. Paints which skin squashes, for a body whose bones do not name
// its soft parts, or to trim what the names found. Kept on the avatar's YAPS Squash
// component, which the squash build reads in place of the bone names.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    public class YapsSquashPainter : EditorWindow
    {
        [MenuItem("Tools/YAPS/Squash regions")]
        static void OpenFromMenu() => Open(Selection.activeGameObject);

        public static void Open(GameObject target)
        {
            var w = GetWindow<YapsSquashPainter>();
            w.titleContent = new GUIContent("Squash regions");
            w.minSize = new Vector2(380, 460);
            // The avatar the selection is part of, so a click on its body still finds it.
            var avatar = target != null ? target.GetComponentInParent<CVRAvatar>(true) : null;
            if (target != null) w.Pick(avatar != null ? avatar.gameObject : target);
        }

        GameObject _avatar;
        SkinnedMeshRenderer _body;
        int _pair, _side, _shape;
        float _radius = 0.04f, _strength = 1f;
        bool _painting, _stroke;
        string _status;
        Tone _tone = Tone.Info;

        // The window painting now, for the brush panel in the Scene view.
        internal static YapsSquashPainter Painting { get; private set; }
        internal static event System.Action Changed;

        // The body as it stands, every copy of a point folded onto the first one, and the
        // working weights per part (pair * 2 + side), per vertex, read on the first copy only.
        Mesh _baked;
        Vector3[] _at;
        int[] _tris, _rep;
        int[][] _near;
        float[][] _w;
        Color[] _colours;
        List<string> _named;
        Material _overlay;

        void OnEnable()
        {
            SceneView.duringSceneGui += OnScene;
            Undo.undoRedoPerformed += Reload;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnScene;
            Undo.undoRedoPerformed -= Reload;
            if (Painting == this) { Painting = null; Changed?.Invoke(); }
            if (_baked != null) DestroyImmediate(_baked);
            if (_overlay != null) DestroyImmediate(_overlay);
        }

        void CreateGUI() => Rebuild();

        YapsSquashSettings Regions => YapsSquash.SettingsOf(_avatar != null ? _avatar.transform : null);

        void Pick(GameObject avatar)
        {
            _avatar = avatar;
            var regions = Regions;
            // The skin the build would use, not the biggest mesh: dense clothing outweighs a body.
            _body = regions != null && regions.body != null ? regions.body
                : avatar == null ? null
                : YapsSquash.NamedBody(avatar.transform) ?? avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null)
                    .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            _pair = _side = 0;
            Load();
            Rebuild();
        }

        void Say(string message, Tone tone)
        {
            _status = message;
            _tone = tone;
            Rebuild();
        }

        // --- the panel ------------------------------------------------------------

        void Rebuild()
        {
            var root = rootVisualElement;
            if (root == null) return;
            root.Clear();
            BridgeElements.Root(root);
            var scroll = BridgeElements.Scroll();
            root.Add(scroll);

            var pick = new BridgeElements.Card("Avatar and body");
            pick.Body.Add(BridgeElements.ObjectPicker<GameObject>("Avatar", _avatar, Pick, "The avatar's top object."));
            pick.Body.Add(BridgeElements.ObjectPicker<SkinnedMeshRenderer>("Body", _body, b =>
            {
                if (Regions != null && Regions.body != null && Regions.pairs.Count > 0 && b != Regions.body)
                {
                    Say("The regions are painted on \"" + Regions.body.name + "\". Remove them first to paint another mesh.", Tone.Warn);
                    return;
                }
                _body = b;
                Load();
                Rebuild();
            }, "The skin to paint. Clothing over it follows the skin under it."));
            // In sight, not the tooltip alone: one field reads as one mesh squashing.
            pick.Body.Add(BridgeElements.Hint("Only the body skin is painted. Clothing and every other mesh over it squash with the skin under them, within 3 cm."));
            if (!string.IsNullOrEmpty(_status)) pick.Body.Add(BridgeElements.Notice(_tone, _status));
            scroll.Add(pick);
            if (_w == null)
            {
                pick.Body.Add(BridgeElements.Hint("Pick an avatar to start."));
                return;
            }

            var regions = Regions;
            int pairs = regions != null ? regions.pairs.Count : 0;
            _pair = Mathf.Clamp(_pair, 0, Mathf.Max(pairs - 1, 0));
            // Top to bottom in the order of the work, all of it here: a step that sent the
            // reader to another menu was a step they lost.
            if (pairs == 0)
            {
                // What the squash uses with nothing painted, said outright: "no regions" beside a
                // build that had just found two pairs read as a contradiction.
                var begin = new BridgeElements.Card("Start");
                if (_named != null)
                {
                    begin.Body.Add(BridgeElements.Hint($"Nothing painted, so the squash uses the {BridgeElements.Count(_named.Count, "pair", "pairs")} its bone names give: {string.Join(", ", _named)}. Paint only to change what they cover."));
                    begin.Body.Add(new BridgeElements.PrimaryButton("Paint over them", FromNames, BridgeTheme.BridgeTo));
                    begin.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Start empty", AddPair, "One empty pair, painted from nothing.")));
                }
                else
                {
                    begin.Body.Add(BridgeElements.Hint("No bone on this body names a breast or butt, so the squash has nothing to squash until you paint it."));
                    begin.Body.Add(BridgeElements.ButtonRow(new BridgeElements.PrimaryButton("Start painting", AddPair, BridgeTheme.BridgeTo)));
                }
                scroll.Add(begin);
                return;
            }

            var paint = new BridgeElements.Card("Paint");
            var names = regions.pairs.Select((p, i) => $"{i + 1}. {p.name}").ToArray();
            paint.Body.Add(BridgeElements.Popup("Pair", "Two parts that press against each other, named after the bones they start from.", names, _pair, i => { _pair = i; Recolour(); Changed?.Invoke(); Rebuild(); }));
            // Greyed out at the cap: a warning on press read as "two is too many".
            var addPair = BridgeElements.Btn("Add a pair", AddPair, "Two at most: the shader has room for four parts.");
            addPair.SetEnabled(pairs < 2);
            paint.Body.Add(BridgeElements.ButtonRow(addPair, BridgeElements.Btn("Remove this pair", RemovePair)));
            paint.Body.Add(BridgeElements.Text("Name", "For the report.", regions.pairs[_pair].name, v =>
            {
                Undo.RecordObject(regions, "Rename squash pair");
                regions.pairs[_pair].name = v;
                EditorUtility.SetDirty(regions);
            }));
            paint.Body.Add(SideChoice(() => Changed?.Invoke()));
            // The brush's own controls sit in the Scene view beside the work, not here.
            paint.Body.Add(new BridgeElements.PrimaryButton(_painting ? "Stop painting" : "Paint in the Scene view", () => SetPainting(!_painting), BridgeTheme.BridgeTo));
            paint.Body.Add(BridgeElements.Hint("The brush and its tools open in the Scene view, over the avatar."));
            paint.Section("Fill this side from");
            paint.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("The selected bone", FromBone, "The skin the bone selected in the Hierarchy and the bones under it carry, onto this side, whole where they carry most.")));
            var mesh = _body.sharedMesh;
            var shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
            if (shapes.Length > 0)
            {
                _shape = Mathf.Clamp(_shape, 0, shapes.Length - 1);
                paint.Body.Add(BridgeElements.Popup("Shape key", "A size or shape slider for the part: the skin it moves.", shapes, _shape, i => _shape = i));
                paint.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("The shape key", FromShape, "The skin that shape key moves, split down the avatar's middle into this pair.")));
            }
            scroll.Add(paint);

            bool built = YapsSquash.Has(_avatar.transform);
            var use = new BridgeElements.Card("Use them");
            use.Body.Add(BridgeElements.Hint(built
                ? "The squash keeps what it was built from until it is rebuilt."
                : "The avatar has no squash yet. Building it uses these regions."));
            use.Body.Add(new BridgeElements.PrimaryButton(built ? "Rebuild the squash" : "Build the squash", () =>
            {
                SetPainting(false);
                string said = YapsSquash.Build(_avatar.transform, out bool ok);
                Say(said, ok ? Tone.Good : Tone.Warn);
            }, BridgeTheme.BridgeTo));
            use.Section("Start over");
            use.Body.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("From the bone names", FromNames, "What the squash finds on its own: breast, boob, butt, booty, glute bones, a pair each, with every bone under them. Replaces every region."),
                BridgeElements.Btn("Remove the regions", () =>
                {
                    // The component keeps its knobs; only the regions go.
                    if (Regions != null)
                    {
                        Undo.RecordObject(Regions, "Remove squash regions");
                        Regions.pairs.Clear();
                        Regions.body = null;
                        EditorUtility.SetDirty(Regions);
                    }
                    Load();
                    Say("Regions removed. A rebuild finds the parts by bone name again.", Tone.Info);
                }, "Back to the bone names on the next build. The knobs stay.", ButtonKind.Danger)));
            scroll.Add(use);
        }

        // In the window and the brush panel both; each tells the other.
        VisualElement SideChoice(System.Action after) => BridgeElements.Choice("Side", "Which of the pair the brush paints.",
            new[] { "The avatar's left", "The avatar's right" }, _side, i =>
            {
                _side = i;
                Recolour();
                after();
            });

        void SetPainting(bool on)
        {
            _painting = on;
            Painting = on ? this : null;
            if (on)
            {
                Bake();
                // The brush shows only under the cursor there, so that is where the work goes.
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Focus();
            }
            Changed?.Invoke();
            Rebuild();
            SceneView.RepaintAll();
        }

        // The brush panel's contents, in the Scene view.
        internal void Brush(VisualElement into)
        {
            var regions = Regions;
            if (regions == null || regions.pairs.Count == 0) return;
            into.Add(BridgeElements.Hint($"Painting {regions.pairs[_pair].name}. Drag over the body, the ring is the brush. Ctrl erases, Esc stops."));
            into.Add(SideChoice(() => rootVisualElement.schedule.Execute(Rebuild)));
            into.Add(BridgeElements.SliderField("Radius (cm)", "How far the brush spreads over the skin.", 1f, 20f, _radius * 100f, v => _radius = v / 100f));
            into.Add(BridgeElements.SliderField("Strength", "How much one stroke paints. Strokes never paint past it.", 0.05f, 1f, _strength, v => _strength = v));
            into.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Wider", () => Each(Grow), "One ring of skin wider."),
                BridgeElements.Btn("Narrower", () => Each(Shrink), "One ring of skin narrower."),
                BridgeElements.Btn("Soften edge", () => Each(Smooth), "So the squash fades out instead of tearing.")));
            into.Add(BridgeElements.ButtonRow(
                BridgeElements.Btn("Copy to the other side", Mirror, "This side onto its partner, across the avatar's middle."),
                BridgeElements.Btn("Clear this side", () => Each((w, c) => System.Array.Clear(w, 0, w.Length)), null, ButtonKind.Danger)));
            into.Add(BridgeElements.Hint("<color=#ff5050>■</color> this side   <color=#5080ff>■</color> its partner   <color=#40cc40>■</color> the other pair"));
            into.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Stop painting", () => SetPainting(false))));
        }

        // --- weights ----------------------------------------------------------------

        // The working weights from the component, or empty ones for this body.
        void Load()
        {
            _w = null;
            _tris = null;
            _named = null;
            if (_body == null || _body.sharedMesh == null) return;
            if (YapsSquash.NamedShares(_avatar.transform, _body, out var named, out _) != null) _named = named;
            var mesh = _body.sharedMesh;
            int n = mesh.vertexCount;
            _rep = Enumerable.Range(0, n).ToArray();
            foreach (var weld in YapsSquash.Welds(mesh))
                foreach (int v in weld) _rep[v] = weld[0];
            var tris = mesh.triangles;
            var near = new HashSet<int>[n];
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = _rep[tris[t + e]], b = _rep[tris[t + (e + 1) % 3]];
                    if (a == b) continue;
                    (near[a] ??= new HashSet<int>()).Add(b);
                    (near[b] ??= new HashSet<int>()).Add(a);
                }
            _near = near.Select(x => x?.ToArray() ?? new int[0]).ToArray();
            _w = Enumerable.Range(0, 4).Select(_ => new float[n]).ToArray();
            var regions = Regions;
            if (regions != null && regions.body == _body)
            {
                // Read against a mesh that changed, every weight would sit on the wrong vertex.
                if (regions.vertexCount != n || regions.fingerprint != YapsSquash.Fingerprint(mesh))
                {
                    _status = "This mesh changed since its regions were painted, so they are not shown and the squash refuses them. Paint again; the first stroke starts over.";
                    _tone = Tone.Warn;
                }
                else
                    for (int k = 0; k < Mathf.Min(regions.pairs.Count, 2); k++)
                        for (int v = 0; v < n; v++)
                        {
                            _w[2 * k][v] = v < regions.pairs[k].a.Length ? regions.pairs[k].a[v] / 255f : 0f;
                            _w[2 * k + 1][v] = v < regions.pairs[k].b.Length ? regions.pairs[k].b[v] / 255f : 0f;
                        }
            }
            Bake();
        }

        void Reload()
        {
            Load();
            Rebuild();
            SceneView.RepaintAll();
        }

        // Into the component, every copy of a point given its first copy's weight.
        void Save(string action)
        {
            var regions = Regions;
            if (regions == null) regions = Undo.AddComponent<YapsSquashSettings>(_avatar);
            else Undo.RecordObject(regions, action);
            regions.body = _body;
            var mesh = _body.sharedMesh;
            regions.vertexCount = mesh.vertexCount;
            regions.fingerprint = YapsSquash.Fingerprint(mesh);
            foreach (var (pair, k) in regions.pairs.Select((p, k) => (p, k)).Take(2))
            {
                pair.a = Bytes(_w[2 * k]);
                pair.b = Bytes(_w[2 * k + 1]);
            }
            EditorUtility.SetDirty(regions);
        }

        byte[] Bytes(float[] w) => Enumerable.Range(0, w.Length).Select(v => (byte)Mathf.RoundToInt(Mathf.Clamp01(w[_rep[v]]) * 255f)).ToArray();

        int Channel => 2 * _pair + _side;

        // Each op on the side being painted, then kept.
        void Each(System.Action<float[], int> op)
        {
            if (Regions == null || Regions.pairs.Count == 0) return;
            op(_w[Channel], Channel);
            Settle(Channel, null);
            Save("Edit squash region");
            Recolour();
        }

        void Grow(float[] w, int c) => Spread(w, (x, n) => Mathf.Max(x, n.Max()));
        void Shrink(float[] w, int c) => Spread(w, (x, n) => Mathf.Min(x, n.Min()));
        void Smooth(float[] w, int c) => Spread(w, (x, n) => 0.5f * x + 0.5f * n.Average());

        void Spread(float[] w, System.Func<float, IEnumerable<float>, float> rule)
        {
            var was = (float[])w.Clone();
            for (int v = 0; v < w.Length; v++)
                if (_rep[v] == v && _near[v].Length > 0) w[v] = rule(was[v], _near[v].Select(x => was[x]));
        }

        // A point is never more than whole: what one part gained comes off the others.
        void Settle(int c, IEnumerable<int> touched)
        {
            foreach (int v in touched ?? Enumerable.Range(0, _w[c].Length))
                for (int o = 0; o < 4; o++)
                    if (o != c) _w[o][v] = Mathf.Min(_w[o][v], 1f - _w[c][v]);
        }

        // This side onto its partner, across the avatar's middle.
        void Mirror()
        {
            if (Regions == null || Regions.pairs.Count == 0) return;
            Bake();
            var root = _avatar.transform;
            var local = _at.Select(p => root.InverseTransformPoint(p)).ToArray();
            const float Cell = 0.01f;
            Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / Cell);
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < local.Length; v++)
                if (_rep[v] == v) { var k = Key(local[v]); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(v); }
            var from = _w[Channel];
            var to = _w[Channel ^ 1];
            for (int v = 0; v < local.Length; v++)
            {
                if (_rep[v] != v) continue;
                var m = Vector3.Scale(local[v], new Vector3(-1f, 1f, 1f));
                var k = Key(m);
                int best = -1;
                float bestD = 0.02f * 0.02f;
                for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
                    if (grid.TryGetValue(k + new Vector3Int(x, y, z), out var l))
                        foreach (int s in l) { float d = (local[s] - m).sqrMagnitude; if (d < bestD) { bestD = d; best = s; } }
                to[v] = best >= 0 ? from[best] : 0f;
            }
            Settle(Channel ^ 1, null);
            Save("Mirror squash region");
            Recolour();
        }

        void AddPair()
        {
            if (_w == null) return;
            if (Regions != null && Regions.pairs.Count >= 2) return;
            Save("Add squash pair");
            var regions = Regions;
            regions.pairs.Add(new YapsSquashSettings.Pair { name = regions.pairs.Count == 0 ? "Chest" : "Seat" });
            _pair = regions.pairs.Count - 1;
            Save("Add squash pair");
            Rebuild();
        }

        void RemovePair()
        {
            var regions = Regions;
            if (regions == null || regions.pairs.Count == 0) return;
            Undo.RecordObject(regions, "Remove squash pair");
            regions.pairs.RemoveAt(_pair);
            // The pair after it moves up a slot, weights and all.
            if (_pair == 0) { _w[0] = _w[2]; _w[1] = _w[3]; }
            _w[2] = new float[_w[2].Length];
            _w[3] = new float[_w[3].Length];
            _pair = 0;
            Save("Remove squash pair");
            Recolour();
            Rebuild();
        }

        void FromNames()
        {
            var shares = YapsSquash.NamedShares(_avatar.transform, _body, out var names, out string none);
            if (shares == null) { Say(none ?? "None of the parts found by name are on this mesh.", Tone.Warn); return; }
            for (int c = 0; c < 4; c++)
                for (int v = 0; v < shares.Length; v++) _w[c][v] = shares[v][c];
            // A point's copies can carry different weights; the first copy's stands for all.
            Save("Squash regions from bone names");
            var regions = Regions;
            Undo.RecordObject(regions, "Squash regions from bone names");
            regions.pairs.Clear();
            foreach (var name in names) regions.pairs.Add(new YapsSquashSettings.Pair { name = name });
            Save("Squash regions from bone names");
            _pair = 0;
            Recolour();
            Say($"Started from {BridgeElements.Count(names.Count, "pair", "pairs")} found by name: {string.Join(", ", names)}, each side with every bone under it.", Tone.Good);
        }

        void FromBone()
        {
            var bone = Selection.activeTransform;
            if (bone == null) { Say("Select a bone in the Hierarchy first.", Tone.Warn); return; }
            var under = new HashSet<Transform>(bone.GetComponentsInChildren<Transform>(true));
            var bones = _body.bones;
            var weights = _body.sharedMesh.boneWeights;
            var w = new float[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var b = weights[v];
                float on = 0f;
                if (Under(b.boneIndex0)) on += b.weight0;
                if (Under(b.boneIndex1)) on += b.weight1;
                if (Under(b.boneIndex2)) on += b.weight2;
                if (Under(b.boneIndex3)) on += b.weight3;
                w[v] = Mathf.Clamp01(on);
            }
            bool Under(int i) => i >= 0 && i < bones.Length && bones[i] != null && under.Contains(bones[i]);
            // Left as it was: an empty side reads as done and only fails at Build.
            float most = w.Max();
            if (most <= 0f) { Say($"\"{bone.name}\" carries none of \"{_body.name}\". Pick the mesh it moves under Body, or another bone.", Tone.Warn); return; }
            // Scaled to its peak as a shape key is: a jiggle bone can carry a part at a tenth, and
            // Build keeps only skin at half or more, so the raw weights dropped the pair.
            for (int v = 0; v < w.Length; v++) w[v] = Mathf.Clamp01(2f * w[v] / most);
            _w[Channel] = w;
            Settle(Channel, null);
            Save("Squash region from a bone");
            Recolour();
            Say($"This side is now the skin \"{bone.name}\" carries.", Tone.Good);
        }

        void FromShape()
        {
            var mesh = _body.sharedMesh;
            int frames = mesh.GetBlendShapeFrameCount(_shape);
            var delta = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(_shape, frames - 1, delta, null, null);
            float most = delta.Max(d => d.magnitude);
            if (most <= 0f) { Say("That shape key moves nothing.", Tone.Warn); return; }
            Bake();
            var root = _avatar.transform;
            int a = 2 * _pair;
            for (int v = 0; v < delta.Length; v++)
            {
                // Twice the share, so the skin the slider moves most is whole and only the edge fades.
                float w = Mathf.Clamp01(2f * delta[v].magnitude / most);
                bool left = root.InverseTransformPoint(_at[v]).x < 0f;
                _w[a][v] = left ? w : 0f;
                _w[a + 1][v] = left ? 0f : w;
            }
            Settle(a, null);
            Settle(a + 1, null);
            Save("Squash regions from a shape key");
            Recolour();
            Say($"This pair is now the skin \"{mesh.GetBlendShapeName(_shape)}\" moves, split down the middle.", Tone.Good);
        }

        // --- the scene view ----------------------------------------------------------

        // The body as it stands, blendshapes and all, in world space.
        void Bake()
        {
            if (_body == null || _w == null) return;
            if (_baked == null) _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _at = WorldPoints(_body, _baked);
            _tris ??= _baked.triangles;
            // BakeMesh puts back the source mesh's colours.
            if (_colours != null && _colours.Length == _at.Length) _baked.colors = _colours;
            else Recolour();
        }

        internal static Vector3[] WorldPoints(SkinnedMeshRenderer body, Mesh into)
        {
            body.BakeMesh(into, true);
            var m = Frame(body);
            return into.vertices.Select(m.MultiplyPoint3x4).ToArray();
        }

        // BakeMesh with useScale divides the scale back out, so the frame carries it again.
        // Position and rotation alone drew the regions off the skin of a scaled avatar.
        internal static Matrix4x4 Frame(SkinnedMeshRenderer body) => body.transform.localToWorldMatrix;

        void Recolour()
        {
            if (_baked == null || _w == null) return;
            int n = _baked.vertexCount;
            if (_colours == null || _colours.Length != n) _colours = new Color[n];
            int c = Channel, partner = c ^ 1, other = c < 2 ? 2 : 0;
            for (int v = 0; v < n; v++)
            {
                int r = _rep[v];
                float on = _w[c][r], with = _w[partner][r], rest = Mathf.Max(_w[other][r], _w[other + 1][r]);
                _colours[v] = new Color(on, rest * 0.8f, with, Mathf.Clamp01(Mathf.Max(on, with, rest) * 0.75f));
            }
            _baked.colors = _colours;
            SceneView.RepaintAll();
        }

        void OnScene(SceneView view)
        {
            if (_baked == null || _body == null || _w == null) return;
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                // Every repaint: in Play mode and under the preview the skin moves, and an
                // overlay baked once sat inside it, out of sight.
                Bake();
                // Faint through whatever hides it, full where it is in sight, so a shader or a pose
                // that moves the skin off the bake never hides a region outright.
                DrawRegions(CompareFunction.Greater, 0.3f);
                DrawRegions(CompareFunction.LessEqual, 1f);
            }
            if (!_painting || Regions == null || Regions.pairs.Count == 0) return;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                SetPainting(false);
                e.Use();
                return;
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hit = Cast(ray, out var at, out int tri);
            if (hit)
            {
                Handles.color = e.control ? Color.cyan : Color.red;
                Handles.DrawWireDisc(at, -ray.direction, _radius);
            }
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.alt || e.button != 0) return;
            if (e.type == EventType.MouseDown && hit)
            {
                // Held by this window, so the drag paints instead of drawing a selection box.
                GUIUtility.hotControl = id;
                Bake();
                if (Cast(ray, out at, out tri)) Dab(at, tri, e.control);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                if (hit) Dab(at, tri, e.control);
                e.Use();
                view.Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                if (_stroke) Save("Paint squash region");
                _stroke = false;
                e.Use();
            }
        }

        void DrawRegions(CompareFunction test, float alpha)
        {
            if (_overlay == null)
            {
                // Vertex colour over the skin, pulled toward the camera so it never fights it.
                _overlay = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _overlay.SetInt("_ZWrite", 0);
                _overlay.SetInt("_Cull", (int)CullMode.Back);
                _overlay.SetFloat("_ZBias", -2f);
            }
            _overlay.SetInt("_ZTest", (int)test);
            _overlay.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            _overlay.SetPass(0);
            Graphics.DrawMeshNow(_baked, Frame(_body));
        }

        // Spreads over the skin from the triangle hit, never across a gap: an arm resting on the
        // side is not painted with it.
        void Dab(Vector3 at, int tri, bool erase)
        {
            var reached = new Dictionary<int, float>();
            var open = new SortedSet<(float d, int v)>();
            for (int k = 0; k < 3; k++)
            {
                int v = _rep[_tris[tri + k]];
                float d = Vector3.Distance(_at[v], at);
                if (!reached.TryGetValue(v, out var was) || d < was) { open.Remove((was, v)); reached[v] = d; open.Add((d, v)); }
            }
            while (open.Count > 0)
            {
                var (d, v) = open.Min;
                open.Remove(open.Min);
                foreach (int n in _near[v])
                {
                    float nd = d + Vector3.Distance(_at[v], _at[n]);
                    if (nd > _radius) continue;
                    if (reached.TryGetValue(n, out var was))
                    {
                        if (nd >= was) continue;
                        open.Remove((was, n));
                    }
                    reached[n] = nd;
                    open.Add((nd, n));
                }
            }
            var w = _w[Channel];
            foreach (var kv in reached)
            {
                if (kv.Value > _radius) continue;
                float x = kv.Value / _radius, f = (1f - x * x) * (1f - x * x) * _strength;
                // Never past the stroke's strength, however long the brush sits there.
                w[kv.Key] = erase ? Mathf.Min(w[kv.Key], 1f - f) : Mathf.Max(w[kv.Key], f);
            }
            Settle(Channel, reached.Keys);
            _stroke = true;
            Recolour();
        }

        // The nearest triangle under the ray, by Moller and Trumbore.
        bool Cast(Ray ray, out Vector3 at, out int tri)
        {
            at = default;
            tri = -1;
            float best = float.MaxValue;
            var o = ray.origin;
            var dir = ray.direction;
            for (int t = 0; t < _tris.Length; t += 3)
            {
                var a = _at[_tris[t]];
                var e1 = _at[_tris[t + 1]] - a;
                var e2 = _at[_tris[t + 2]] - a;
                var p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                var s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float d = Vector3.Dot(e2, q) * inv;
                if (d > 0f && d < best) { best = d; tri = t; }
            }
            if (tri < 0) return false;
            at = o + dir * best;
            return true;
        }
    }

    // The brush's controls in the Scene view, shown only while a Squash regions window paints.
    [Overlay(typeof(SceneView), "yaps-squash-brush", "Squash brush")]
    class YapsSquashBrush : UnityEditor.Overlays.Overlay, ITransientOverlay
    {
        VisualElement _panel;

        public bool visible => YapsSquashPainter.Painting != null;

        public override VisualElement CreatePanelContent()
        {
            _panel = BridgeElements.Root(new VisualElement());
            _panel.style.width = 300;
            YapsSquashPainter.Changed -= Fill;
            YapsSquashPainter.Changed += Fill;
            Fill();
            return _panel;
        }

        void Fill()
        {
            if (_panel == null) return;
            _panel.Clear();
            if (YapsSquashPainter.Painting != null) YapsSquashPainter.Painting.Brush(_panel);
        }
    }
}
#endif
