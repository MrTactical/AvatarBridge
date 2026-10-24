// The YAPS Squash component's inspector, in steps: the parts with the region painter, the knobs
// (written straight to the build's materials) and the build last. The preview that presses the parts as the game does. Another player's hands, written into
// the client's own globals, squeeze the pair from outside and press where dragged, in edit and
// Play mode alike. Edit mode runs no cloth, so there the preview also swings the rests under it.
#if CVR_CCK_EXISTS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    [CustomEditor(typeof(YapsSquashSettings))]
    public class YapsSquashEditor : Editor
    {
        VisualElement _root;
        string _said;
        Tone _saidTone;
        // Kept across redraws, so a stroke in progress and the overlay survive them; gone with
        // the inspector.
        YapsSquashPainter _painter;
        bool _pickBones;

        void OnDisable()
        {
            if (_painter != null) DestroyImmediate(_painter);
        }

        // One short line in each fold's header. The knobs keep their own tooltips.
        static readonly System.Collections.Generic.Dictionary<string, string> FoldSummaries = new System.Collections.Generic.Dictionary<string, string>
        {
            { "How it gives", "how far it flattens and where the volume goes" },
            { "The pair", "whether the two meet in the middle, on by default" },
            { "In motion", "stretch and squash as it swings, real to anime" },
            { "What presses it", "arms, hands, plugs and cloth" },
        };

        // Inside each fold: what it is and how to see it, which the header alone never said.
        static readonly System.Collections.Generic.Dictionary<string, string> FoldHints = new System.Collections.Generic.Dictionary<string, string>
        {
            { "How it gives", "How a part answers any press: how flat it goes, how wide it spreads to keep its volume, how far it swings away first. See it with Preview and Squeeze or the test hand." },
            { "The pair", "One part swung into a still partner shunts it aside, so both move its way. Pressed together from both sides, each part swings apart, then flattens and keeps its volume; what still overlaps passes through. Meet presses the two flat where they touch, rounded at the edge. See it with Preview and Squeeze." },
            { "In motion", "As the physics swing a part, it stretches away from where it hangs and squashes back. See it with Preview and Bounce, or move the avatar in Play mode." },
            { "What presses it", "What dents the parts besides each other, each with its own strength. See hands with the test hand." },
        };

        static readonly string[] HereNotKnobs = { "partBones", "leaveAlone" };

        const string Animatable =
            "material._YAPS_DentPower        0 off, 1 on\n" +
            "material._YAPS_SquashTune.y     strength\n" +
            "material._YAPS_SquashMotion.x   stretch in motion, 0 off\n" +
            "material._YAPS_SquashHands.x    own arms\n" +
            "material._YAPS_SquashHands.z    other players' hands\n" +
            "material._YAPS_SquashPlug       plugs";

        public override VisualElement CreateInspectorGUI()
        {
            _root = BridgeElements.Root(new VisualElement(), inspector: true);
            Rebuild();
            return _root;
        }

        void RebuildLater() => _root?.schedule.Execute(Rebuild);

        static string LeftAlone(int n) => n == 0 ? "none" : BridgeElements.Count(n, "material", "materials");

        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            // A stale inspector, its component gone with the scene Play mode reloaded.
            if (target == null) return;
            serializedObject.Update();
            var settings = (YapsSquashSettings) target;
            var root = YapsSquash.RootOf(settings);
            bool built = YapsSquash.Has(root);
            int mats = YapsSquash.Materials(root).Count;
            bool painted = settings.pairs.Count > 0;
            bool stale = built && YapsSquash.RegionsHash(settings) != settings.builtFrom;

            _root.Add(BridgeElements.Banner("Squash",
                !built ? "not built yet: work down the steps, then Build at the bottom"
                : stale ? $"built · {BridgeElements.Count(mats, "material", "materials")} · parts changed since: Rebuild at the bottom"
                : $"built · {BridgeElements.Count(mats, "material", "materials")} · parts {YapsSquash.PartsFrom(settings)}",
                built ? "built" : "not built", BridgeTheme.Span.Cvr));
            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);
            body.Add(BridgeElements.Notice(Tone.Info, YapsInspectorStyle.InertComponentNote));

            // Top to bottom in the order of the work, building last as in the other windows:
            // what squashes, how it feels, then build.
            var parts = new BridgeElements.Card("Which parts squash").Step(1, 3, BridgeTheme.Span.Cvr);
            body.Add(parts);
            // One step whatever the parts come from, said first. As two steps, the first said the
            // paint wins and the second "only if the bones miss", side by side.
            bool picked = settings.partBones.Count > 0;
            if (_painter == null && painted) _painter = YapsSquashPainter.For(root.gameObject, RebuildLater);
            if (painted)
                parts.Body.Add(BridgeElements.Hint($"Painted: {BridgeElements.Count(settings.pairs.Count, "pair", "pairs")} on \"{(settings.body != null ? settings.body.name : "?")}\", which win over any bones."));
            else if (picked)
                parts.Body.Add(BridgeElements.Hint("From the bones picked below, two to a pair, one each side."));
            // An open painter names what the bone names give, so this would say it twice.
            else if (_painter == null)
                parts.Body.Add(BridgeElements.Hint("Found by bone name: breast, boob, butt, booty or glute, one each side. Paint them, or pick the bones, for anything else."));

            // The painter lives here, not in a window of its own: a step that sent the reader to
            // another window was a step they lost.
            if (_painter != null) _painter.Panel(parts.Body);
            else
            {
                var paint = BridgeElements.Btn("Paint them", () =>
                {
                    _painter = YapsSquashPainter.For(root.gameObject, RebuildLater);
                    RebuildLater();
                }, "Paint which skin squashes on the body, starting from what the bones give. A rebuild picks it up.");
                parts.Body.Add(picked || _pickBones ? BridgeElements.ButtonRow(paint) : BridgeElements.ButtonRow(paint,
                    BridgeElements.Btn("Pick bones", () => { _pickBones = true; RebuildLater(); },
                        "Pick by hand the bones of anything that comes in two: cheeks, thighs.")));
            }

            // In sight only while it decides something: two empty lists read as two jobs left.
            if (picked || (_pickBones && !painted))
            {
                if (painted) parts.Body.Add(BridgeElements.Hint("The painting wins, so these bones are not used."));
                var bones = new PropertyField(serializedObject.FindProperty("partBones"), "Part bones");
                // The line above names the source, so it follows the list.
                bones.TrackPropertyValue(serializedObject.FindProperty("partBones"), p => RebuildLater());
                parts.Body.Add(bones);
            }

            // Needs a rebuild like the parts, so it sits here and not with the knobs.
            var leaveProp = serializedObject.FindProperty("leaveAlone");
            var leave = new BridgeElements.Card("Materials left alone", LeftAlone(leaveProp.arraySize), false)
                .Nested().Remember("Yaps.Squash.LeftAlone");
            parts.Body.Add(leave);
            leave.Body.Add(BridgeElements.Hint("Layers the squash never touches. They stay still while the skin under them squashes, so a press can show the skin through one."));
            var leaveField = new PropertyField(leaveProp, "Materials");
            leaveField.TrackPropertyValue(leaveProp, p => leave.SetSummary(LeftAlone(p.arraySize)));
            leave.Body.Add(leaveField);

            // The preview and the knobs in one card: a knob shows nothing until something presses.
            var feel = new BridgeElements.Card("Tune how it feels").Step(2, 3, BridgeTheme.Span.Cvr);
            body.Add(feel);
            YapsSquashPreview.Controls(feel.Body, settings, RebuildLater);
            feel.Body.Add(BridgeElements.Hint(YapsSquashPreview.On == settings
                ? "Knobs write to the materials as you move them, no rebuild, and show on the preview."
                : built ? "Knobs write to the materials as you move them, no rebuild. Preview first to see them."
                : "Knobs set now are used by the build. Build in step 3, then Preview here to see them."));

            // Fields in declaration order, a fold per [Header]. The regions are hidden.
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            BridgeElements.Card fold = null;
            while (it.NextVisible(false))
            {
                if (HereNotKnobs.Contains(it.name)) continue;
                var field = typeof(YapsSquashSettings).GetField(it.name);
                if (field?.GetCustomAttributes(typeof(HeaderAttribute), false).FirstOrDefault() is HeaderAttribute h)
                {
                    FoldSummaries.TryGetValue(h.header, out string summary);
                    fold = new BridgeElements.Card(h.header, summary, fold == null).Nested().Remember("Yaps.Squash." + h.header);
                    feel.Body.Add(fold);
                    if (FoldHints.TryGetValue(h.header, out string hint)) fold.Body.Add(BridgeElements.Hint(hint));
                }
                (fold != null ? fold.Body : feel.Body).Add(YapsInspectorStyle.Field(it.Copy(), field));
            }

            var animate = new BridgeElements.Card("Animate it", "turn it off or change it from a toggle", false).Nested();
            feel.Body.Add(animate);
            animate.Body.Add(BridgeElements.Hint(
                "Key these under each renderer the squash reaches ▸ Material, on all of them alike: a layer left squashing over skin that is not comes apart from it."));
            animate.Body.Add(BridgeElements.Code(Animatable));

            var build = new BridgeElements.Card(built ? "Rebuild" : "Build").Step(3, 3, BridgeTheme.Span.Cvr);
            body.Add(build);
            build.Body.Add(BridgeElements.Hint(built
                ? "Rebuild after step 1 changes, the body or its bones change, or a new outfit is added. Knobs need no rebuild."
                : "Measures the parts and patches every layer over them. Shaders already carrying YAPS keep theirs."));
            build.Body.Add(new BridgeElements.PrimaryButton(built ? "Rebuild the squash" : "Build the squash", () =>
            {
                YapsSquashPreview.Stop();
                if (_painter != null && _painter.IsPainting) _painter.SetPainting(false);
                _said = YapsSquash.Build(root, out bool ok);
                if (ok) _said += " Preview it in step 2.";
                _saidTone = ok ? Tone.Good : Tone.Warn;
                RebuildLater();
            }, BridgeTheme.BridgeTo));
            if (_said != null) build.Body.Add(BridgeElements.Notice(_saidTone, _said));
            if (built)
            {
                build.Section("Remove");
                build.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Remove the squash", () =>
                {
                    YapsSquashPreview.Stop();
                    bool removed = YapsSquash.Strip(root, YapsSwapFollow.RunnableClips(root));
                    _said = removed ? "Squash removed. Every layer and toggle is back on its own material; the knobs stay for the next build."
                        : YapsNativeBuilder.NotInPlayMode;
                    _saidTone = removed ? Tone.Good : Tone.Warn;
                    RebuildLater();
                }, null, ButtonKind.Danger)));
            }

            _root.Bind(serializedObject);
            // No undo record on the materials: the component owns the values and its own undo
            // replays here.
            body.TrackSerializedObjectValue(serializedObject, so =>
            {
                YapsSquash.Apply(root);
                SceneView.RepaintAll();
            });
        }
    }

    // Writes the slots and the hand globals itself while on. Nothing it writes is saved: the
    // arrays are not serialised, and Stop puts them back. In Play mode the providers own the
    // slots, so only the hand is written.
    static class YapsSquashPreview
    {
        public static YapsSquashSettings On { get; private set; }
        public static float Squeeze;
        public static bool Bounce, Hand;
        // The hand's player's viewpoint height, which sizes the hand as it does in the shader.
        // Kept between previews, unlike the rest.
        public static float Tall = 1.7f;
        // The avatar the component sits under, and the dragged hand in its space, so the hand
        // walks, jumps and turns with it.
        static Transform _root;
        static Vector3 _wrist;
        static Vector4 _params;
        static readonly Matrix4x4[] Empty = new Matrix4x4[20];
        static readonly Vector4[] NoOne = new Vector4[255];

        // The preview's controls, for the component's inspector and the Avatar Tester alike.
        // Returns the Preview button's row, so a caller can put its own buttons beside it.
        public static VisualElement Controls(VisualElement into, YapsSquashSettings settings, System.Action refresh)
        {
            bool built = YapsSquash.Has(YapsSquash.RootOf(settings));
            into.Add(Application.isPlaying
                ? BridgeElements.Hint("The cloth is real in Play mode: move the avatar (Jump or Shake in the Avatar Tester) and it swings and stretches. Squeeze and the test hand press it as another player's hands would.",
                    "Drag the hand with its handle in the scene view; it moves with the avatar. Pause Play mode to hold the cloth still while it presses.")
                : BridgeElements.Hint("Squeezes, bounces and presses it in the scene view.",
                    "Squeeze is another player's two hands pushing each pair together from outside, the test hand one more you drag. " +
                    "Edit mode runs no physics, so Bounce stands in for the swing a jump gives. In game the same knobs answer real hands."));
            bool on = On == settings;
            var preview = BridgeElements.Btn(on ? "Stop the preview" : "Preview", () =>
            {
                if (On == settings) Stop(); else Start(settings);
                refresh();
            }, built ? null : "Build first");
            preview.SetEnabled(built);
            var row = BridgeElements.ButtonRow(preview);
            into.Add(row);
            if (!on) return row;
            var squeeze = new Slider("Squeeze", 0f, 1f) { value = Squeeze, showInputField = true,
                tooltip = "Two open hands pushing each pair together from outside, how far in. They press as other players' hands do, so \"Other players' hands\" scales them." };
            squeeze.AddToClassList("ab-slider");
            squeeze.RegisterValueChangedCallback(e => Squeeze = e.newValue);
            into.Add(squeeze);
            if (!Application.isPlaying)
                into.Add(BridgeElements.Bind("Bounce", "Swings each part up and down under it, as a jump would, to show the stretch in motion.",
                    Bounce, b => Bounce = b));
            into.Add(BridgeElements.Bind("Test hand", "Another player's open hand, dragged with the handle in the scene view. It presses the part nearest it.",
                Hand, b => { Hand = b; SceneView.RepaintAll(); }));
            into.Add(BridgeElements.SliderField("Player height", "How tall the hand's player is, in metres. A hand is sized to its player, so lower it for a smaller hand pressing a smaller spot.",
                0.3f, 2.5f, Tall, v => { Tall = v; SceneView.RepaintAll(); }));
            return row;
        }

        public static void Start(YapsSquashSettings s)
        {
            Stop();
            var root = YapsSquash.RootOf(s);
            var slots = YapsSquash.Slots(root);
            if (slots == null) return;
            On = s;
            _root = root;
            // Pressed from the start, so a knob shows as soon as it moves.
            Squeeze = 0.5f;
            _params = Shader.GetGlobalVector("CVRGlobalParams1");
            // In front of the first part, a hand's length and a palm out.
            var part = slots[0] != null ? slots[0].position : root.position + Vector3.up;
            _wrist = root.InverseTransformPoint(part + root.forward * 0.25f);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Leave;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            // Here and not in an inspector, so the handle stays when the tester runs it.
            SceneView.duringSceneGui += Draw;
        }

        static void Leave(PlayModeStateChange _) => Stop();

        public static void Stop()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= Leave;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            SceneView.duringSceneGui -= Draw;
            if (On == null) return;
            if (!Application.isPlaying && _root != null)
                foreach (var m in YapsSquash.Materials(_root)) m.SetMatrixArray("_YAPS_SquashFollow", Empty);
            foreach (var name in new[] { "_CVR_PlayerLeftHandPositions", "_CVR_PlayerRightHandPositions", "_CVR_PlayerChestPositions", "_CVR_PlayerRootPositions" })
                Shader.SetGlobalVectorArray(name, NoOne);
            Shader.SetGlobalVector("CVRGlobalParams1", _params);
            On = null;
            _root = null;
            Squeeze = 0f;
            Bounce = Hand = false;
            SceneView.RepaintAll();
        }

        static void Tick()
        {
            if (On == null || _root == null) { Stop(); return; }
            var root = _root;
            var slots = YapsSquash.Slots(root);
            var mats = YapsSquash.Materials(root);
            if (slots == null || mats.Count == 0) { Stop(); return; }
            var m = slots.Select(t => t != null ? t.localToWorldMatrix : Matrix4x4.zero).ToArray();
            if (!Application.isPlaying) Stand(root, m, mats);
            Press(root, slots, mats[0].GetVector("_YAPS_SquashReach"), Nearest(slots, root.TransformPoint(_wrist)));
            SceneView.RepaintAll();
        }

        // The stand-in for the cloth edit mode never runs. Squeeze is not one: moving the follows
        // together left the skin where it was, so the shader read an overlap and swung the pair
        // apart, the opposite of a squeeze. Hands press the skin as the game's do.
        static void Stand(Transform root, Matrix4x4[] m, System.Collections.Generic.List<Material> mats)
        {
            var reach = mats[0].GetVector("_YAPS_SquashReach");
            float swing = Mathf.Sin((float) EditorApplication.timeSinceStartup * 2f * Mathf.PI * 1.4f);
            for (int i = 0; i < 4; i++)
            {
                float unit = ((Vector3) m[i].GetColumn(0)).magnitude;
                if (unit <= 0f || reach[i] <= 0f) continue;
                // The rest moved the other way gives the same swing with the part where its skin is.
                if (Bounce) m[4 + i] = Matrix4x4.Translate(-root.up * (0.5f * reach[i] * unit * swing)) * m[4 + i];
            }
            foreach (var mat in mats) mat.SetMatrixArray("_YAPS_SquashFollow", m);
        }

        // The part the hand reaches for. The first part always was, so a hand dragged onto a
        // second pair faced up toward the first and never pressed in.
        static Vector3 Nearest(Transform[] slots, Vector3 wrist) =>
            slots.Take(4).Where(t => t != null && t.lossyScale.sqrMagnitude > 0f).Select(t => t.position)
                .OrderBy(p => (p - wrist).sqrMagnitude).DefaultIfEmpty(wrist + Vector3.forward).First();

        // Player 1's open left hand, reaching from a chest behind it toward the part, and for
        // Squeeze players 0 and 2, one per pair, a hand each side pushing it together.
        static void Press(Transform root, Transform[] slots, Vector4 reach, Vector3 part)
        {
            var left = new Vector4[255];
            var right = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            if (Hand)
            {
                var wrist = root.TransformPoint(_wrist);
                var aim = (part - wrist).normalized;
                var chest = wrist - aim * 0.45f;
                left[1] = new Vector4(wrist.x, wrist.y, wrist.z, -1f);
                chests[1] = chest;
                roots[1] = new Vector4(chest.x, 0f, chest.z, Tall);
            }
            if (Squeeze > 0f)
                for (int i = 0; i < 4; i++)
                {
                    if (!Squeezer(slots, reach, i, out var wrist, out var chest)) continue;
                    int player = i & 2;
                    ((i & 1) == 0 ? left : right)[player] = new Vector4(wrist.x, wrist.y, wrist.z, -1f);
                    chests[player] = chest;
                    roots[player] = new Vector4(chest.x, 0f, chest.z, Tall);
                }
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", right);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(_params.x, Hand || Squeeze > 0f ? 3f : 0f, _params.z, _params.w));
        }

        // An open hand on the far side of part i from its partner, fingers up so its side presses:
        // touching at Squeeze 0, and at 1 pushed in to the part's centre. Further, a quarter of the
        // way to the partner, pinched the pair's bottoms into a bridge. Its chest sits below the
        // wrist only to point the fingers up.
        static bool Squeezer(Transform[] slots, Vector4 reach, int i, out Vector3 wrist, out Vector3 chest)
        {
            wrist = chest = Vector3.zero;
            var t = slots[i];
            var partner = slots[i ^ 1];
            if (t == null || partner == null || reach[i] <= 0f) return false;
            var away = t.position - partner.position;
            float gap = away.magnitude;
            if (gap < 1e-5f) return false;
            away /= gap;
            float r = On.handSize * Tall / 1.6f;
            float part = reach[i] * t.lossyScale.x;
            float d = Mathf.Lerp(part + r, r, Squeeze);
            float length = Mathf.Max(0.11f * Tall - r, 0f);
            wrist = t.position + away * d - _root.up * (0.5f * length);
            chest = wrist - _root.up * 0.45f;
            return true;
        }

        // The wrist, and the palm the shader presses with drawn ahead of it.
        static void Draw(SceneView view)
        {
            if (On == null || !Hand || _root == null) return;
            var slots = YapsSquash.Slots(_root);
            if (slots == null || slots[0] == null) return;
            var root = _root;
            var wrist = root.TransformPoint(_wrist);
            var aim = (Nearest(slots, wrist) - wrist).normalized;
            float radius = On.handSize * Tall / 1.6f;
            Handles.color = YapsInspectorStyle.PlugColour;
            Handles.DrawWireDisc(wrist + aim * (0.06f * Tall / 1.7f), aim, radius);
            Handles.DrawLine(wrist, wrist + aim * (0.11f * Tall));
            _wrist = root.InverseTransformPoint(Handles.PositionHandle(wrist, Quaternion.LookRotation(aim)));
        }
    }
}
#endif
