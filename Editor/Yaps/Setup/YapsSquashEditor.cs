// The YAPS Squash component's inspector: the knobs, written straight to the build's materials,
// and a preview that moves the slots the provider moves in game. Edit mode runs no cloth, so the
// preview squeezes the pair and swings the rests under it; in Play mode the cloth is real and the
// preview only adds a test hand where the client would put another player's.
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

        // One short line in each fold's header. The knobs keep their own tooltips.
        static readonly System.Collections.Generic.Dictionary<string, string> FoldSummaries = new System.Collections.Generic.Dictionary<string, string>
        {
            { "How it gives", "how far it flattens and where the volume goes" },
            { "The pair", "whether the two meet in the middle, off by default" },
            { "In motion", "stretch and squash as it swings, real to anime" },
            { "What presses it", "arms, hands, plugs and cloth" },
        };

        // Inside each fold: what it is and how to see it, which the header alone never said.
        static readonly System.Collections.Generic.Dictionary<string, string> FoldHints = new System.Collections.Generic.Dictionary<string, string>
        {
            { "How it gives", "How a part answers any press: how flat it goes, how wide it spreads to keep its volume, how far it swings away first. See it with Preview and Squeeze or the test hand." },
            { "The pair", "Pressed together, each part of a pair flattens and keeps its volume; what still overlaps passes through. Meet presses the two onto one plane instead, a hard seam. See it with Preview and Squeeze." },
            { "In motion", "As the physics swing a part, it stretches away from where it hangs and squashes back. See it with Preview and Bounce, or move the avatar in Play mode." },
            { "What presses it", "What dents the parts besides each other, each with its own strength. See hands with the test hand." },
        };

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

        void Rebuild()
        {
            if (_root == null) return;
            _root.Clear();
            serializedObject.Update();
            var settings = (YapsSquashSettings) target;
            var root = settings.transform;
            bool built = YapsSquash.Has(root);
            int mats = YapsSquash.Materials(root).Count;
            bool painted = settings.pairs.Count > 0;

            _root.Add(BridgeElements.Banner("Squash",
                built ? $"built · {BridgeElements.Count(mats, "material", "materials")} · parts {(painted ? "painted" : "by bone name")}" : "not built yet: press Build at the bottom",
                built ? "built" : "not built", BridgeTheme.Span.Cvr));
            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);
            body.Add(BridgeElements.Notice(Tone.Info, YapsInspectorStyle.InertComponentNote));

            var see = new BridgeElements.Card("See it work");
            body.Add(see);
            YapsSquashPreview.Controls(see.Body, settings, RebuildLater);

            var parts = new BridgeElements.Card("Parts");
            body.Add(parts);
            parts.Body.Add(BridgeElements.Hint(painted
                ? $"From {BridgeElements.Count(settings.pairs.Count, "painted pair", "painted pairs")} on \"{(settings.body != null ? settings.body.name : "?")}\"."
                : "Found by bone name: breast, boob, butt, booty or glute, one each side. Paint regions for a body whose bones do not say."));
            parts.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Paint regions", () => YapsSquashPainter.Open(root.gameObject),
                "Paint which skin squashes. A rebuild picks them up.")));

            // Fields in declaration order, a fold per [Header]. The regions are hidden.
            var feel = new BridgeElements.Card("How it feels");
            body.Add(feel);
            feel.Body.Add(BridgeElements.Hint("Knobs write to the materials as you move them: no rebuild."));
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            BridgeElements.Card fold = null;
            while (it.NextVisible(false))
            {
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

            var build = new BridgeElements.Card("Build");
            body.Add(build);
            build.Body.Add(BridgeElements.Hint(built
                ? "Rebuild after the body, its bones or its regions change, or after a new outfit is added."
                : "Measures the parts and patches every layer over them. Shaders already carrying YAPS keep theirs."));
            build.Body.Add(new BridgeElements.PrimaryButton(built ? "Rebuild" : "Build", () =>
            {
                YapsSquashPreview.Stop();
                _said = YapsSquash.Build(root, out bool ok);
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
                    YapsSquash.Strip(root, YapsSwapFollow.RunnableClips(root));
                    _said = "Squash removed. Every layer and toggle is back on its own material; the knobs stay for the next build.";
                    _saidTone = Tone.Good;
                    RebuildLater();
                }, null, ButtonKind.Danger)));
            }

            var animate = new BridgeElements.Card("Animate it", null, false);
            body.Add(animate);
            animate.Body.Add(BridgeElements.Hint(
                "Key these under each renderer the squash reaches ▸ Material, on all of them alike: a layer left squashing over skin that is not comes apart from it."));
            animate.Body.Add(BridgeElements.Code(Animatable));

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
        // In the avatar's space, so the hand walks, jumps and turns with it.
        static Vector3 _wrist;
        static Vector4 _params;
        static readonly Matrix4x4[] Empty = new Matrix4x4[20];
        static readonly Vector4[] NoOne = new Vector4[255];

        // The preview's controls, for the component's inspector and the Avatar Tester alike.
        // Returns the Preview button's row, so a caller can put its own buttons beside it.
        public static VisualElement Controls(VisualElement into, YapsSquashSettings settings, System.Action refresh)
        {
            bool built = YapsSquash.Has(settings.transform);
            into.Add(Application.isPlaying
                ? BridgeElements.Hint("The cloth is real in Play mode: move the avatar (Jump or Shake in the Avatar Tester) and it swings and stretches. The preview adds another player's open hand.",
                    "Drag the hand with its handle in the scene view; it moves with the avatar. Pause Play mode to hold the cloth still while the hand presses. " +
                    "To hold the pair squeezed together, leave Play mode: there Squeeze keeps them pressed at any amount.")
                : BridgeElements.Hint("Squeezes, bounces and presses it in the scene view.",
                    "Edit mode runs no physics, so this stands in for them: the pair pushed together, the swing a bounce gives, " +
                    "and another player's open hand, which you drag. In Play mode the cloth is real. In game the same knobs answer the real ones."));
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
            if (!Application.isPlaying)
            {
                var squeeze = new Slider("Squeeze", 0f, 1f) { value = Squeeze, showInputField = true,
                    tooltip = "The pair pushed together, as a share of the way." };
                squeeze.AddToClassList("ab-slider");
                squeeze.RegisterValueChangedCallback(e => Squeeze = e.newValue);
                into.Add(squeeze);
                into.Add(BridgeElements.Bind("Bounce", "Swings each part up and down under it, as a jump would, to show the stretch in motion.",
                    Bounce, b => Bounce = b));
            }
            into.Add(BridgeElements.Bind("Test hand", "Another player's open hand, dragged with the handle in the scene view. It presses the part nearest it.",
                Hand, b => { Hand = b; SceneView.RepaintAll(); }));
            return row;
        }

        public static void Start(YapsSquashSettings s)
        {
            Stop();
            var slots = YapsSquash.Slots(s.transform);
            if (slots == null) return;
            On = s;
            _params = Shader.GetGlobalVector("CVRGlobalParams1");
            // In front of the first part, a hand's length and a palm out.
            var part = slots[0] != null ? slots[0].position : s.transform.position + Vector3.up;
            _wrist = s.transform.InverseTransformPoint(part + s.transform.forward * 0.25f);
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
            if (!Application.isPlaying)
                foreach (var m in YapsSquash.Materials(On.transform)) m.SetMatrixArray("_YAPS_SquashFollow", Empty);
            foreach (var name in new[] { "_CVR_PlayerLeftHandPositions", "_CVR_PlayerRightHandPositions", "_CVR_PlayerChestPositions", "_CVR_PlayerRootPositions" })
                Shader.SetGlobalVectorArray(name, NoOne);
            Shader.SetGlobalVector("CVRGlobalParams1", _params);
            On = null;
            Squeeze = 0f;
            Bounce = Hand = false;
            SceneView.RepaintAll();
        }

        static void Tick()
        {
            if (On == null) { Stop(); return; }
            var root = On.transform;
            var slots = YapsSquash.Slots(root);
            var mats = YapsSquash.Materials(root);
            if (slots == null || mats.Count == 0) { Stop(); return; }
            var m = slots.Select(t => t != null ? t.localToWorldMatrix : Matrix4x4.zero).ToArray();
            if (!Application.isPlaying) Stand(root, m, mats);
            Press(root, Nearest(slots, root.TransformPoint(_wrist)));
            SceneView.RepaintAll();
        }

        // The stand-ins for the cloth edit mode never runs.
        static void Stand(Transform root, Matrix4x4[] m, System.Collections.Generic.List<Material> mats)
        {
            var reach = mats[0].GetVector("_YAPS_SquashReach");
            float swing = Mathf.Sin((float) EditorApplication.timeSinceStartup * 2f * Mathf.PI * 1.4f);
            for (int i = 0; i < 4; i++)
            {
                float unit = ((Vector3) m[i].GetColumn(0)).magnitude;
                if (unit <= 0f || reach[i] <= 0f) continue;
                // Each follow toward its partner. The skin stays put, so the squash is drawn
                // about where the follow moved to, as it is in game when the cloth swings in.
                var partner = (Vector3) m[i ^ 1].GetColumn(3);
                var to = Vector3.Lerp(m[i].GetColumn(3), partner, 0.3f * Squeeze);
                m[i].SetColumn(3, new Vector4(to.x, to.y, to.z, 1f));
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

        // Player 1's open left hand, reaching from a chest behind it toward the part.
        static void Press(Transform root, Vector3 part)
        {
            var left = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            var wrist = root.TransformPoint(_wrist);
            var aim = (part - wrist).normalized;
            var chest = wrist - aim * 0.45f;
            left[1] = new Vector4(wrist.x, wrist.y, wrist.z, -1f);
            chests[1] = chest;
            roots[1] = new Vector4(chest.x, 0f, chest.z, 1.7f);
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", NoOne);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(_params.x, Hand ? 2f : 0f, _params.z, _params.w));
        }

        // The wrist, and the palm the shader presses with drawn ahead of it.
        static void Draw(SceneView view)
        {
            if (On == null || !Hand) return;
            var slots = YapsSquash.Slots(On.transform);
            if (slots == null || slots[0] == null) return;
            var root = On.transform;
            var wrist = root.TransformPoint(_wrist);
            var aim = (Nearest(slots, wrist) - wrist).normalized;
            float radius = On.handSize * 1.7f / 1.6f;
            Handles.color = YapsInspectorStyle.PlugColour;
            Handles.DrawWireDisc(wrist + aim * 0.06f, aim, radius);
            Handles.DrawLine(wrist, wrist + aim * (0.11f * 1.7f));
            _wrist = root.InverseTransformPoint(Handles.PositionHandle(wrist, Quaternion.LookRotation(aim)));
        }
    }
}
#endif
