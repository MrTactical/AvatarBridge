// The YAPS Squash component's inspector: the knobs, written straight to the build's materials,
// and an edit mode preview that moves the slots the provider moves in game. The editor never
// runs the cloth, so the preview squeezes the pair, swings the rests under it, and puts a test
// hand where the client would put another player's.
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
            { "The pair", "the two meeting in the middle" },
            { "In motion", "stretch and squash as it swings, real to anime" },
            { "What presses it", "arms, hands, plugs and cloth" },
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
                built ? $"built · {mats} material(s) · parts {(painted ? "painted" : "by bone name")}" : "not built yet: press Build at the bottom",
                built ? "built" : "not built", BridgeTheme.Span.Cvr));
            var body = new VisualElement();
            body.AddToClassList("ab-scroll");
            _root.Add(body);
            body.Add(BridgeElements.Notice(Tone.Info, YapsInspectorStyle.InertComponentNote +
                " To switch the squash, animate the materials' _YAPS_DentPower (0 to 1)."));

            var see = new BridgeElements.Card("See it work");
            body.Add(see);
            see.Body.Add(BridgeElements.Hint("Squeezes, bounces and presses it in the scene view, editor only.",
                "The editor runs no physics, so this stands in for them: the pair pushed together, the swing a bounce gives, " +
                "and another player's open hand, which you drag. In game the same knobs answer the real ones."));
            bool on = YapsSquashPreview.On == settings;
            var preview = BridgeElements.Btn(on ? "Stop the preview" : "Preview", () =>
            {
                if (YapsSquashPreview.On == settings) YapsSquashPreview.Stop(); else YapsSquashPreview.Start(settings);
                RebuildLater();
            }, built ? null : "Build first");
            preview.SetEnabled(built);
            see.Body.Add(BridgeElements.ButtonRow(preview));
            if (on)
            {
                var squeeze = new Slider("Squeeze", 0f, 1f) { value = YapsSquashPreview.Squeeze, showInputField = true,
                    tooltip = "The pair pushed together, as a share of the way." };
                squeeze.AddToClassList("ab-slider");
                squeeze.RegisterValueChangedCallback(e => YapsSquashPreview.Squeeze = e.newValue);
                see.Body.Add(squeeze);
                see.Body.Add(BridgeElements.Bind("Bounce", "Swings each part up and down under it, as a jump would, to show the stretch in motion.",
                    YapsSquashPreview.Bounce, b => YapsSquashPreview.Bounce = b));
                see.Body.Add(BridgeElements.Bind("Test hand", "Another player's open hand, dragged with the handle in the scene view.",
                    YapsSquashPreview.Hand, b => { YapsSquashPreview.Hand = b; SceneView.RepaintAll(); }));
            }

            var parts = new BridgeElements.Card("Parts");
            body.Add(parts);
            parts.Body.Add(BridgeElements.Hint(painted
                ? $"From {settings.pairs.Count} painted pair(s) on \"{(settings.body != null ? settings.body.name : "?")}\"."
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

        void OnSceneGUI()
        {
            var settings = (YapsSquashSettings) target;
            if (YapsSquashPreview.On != settings || !YapsSquashPreview.Hand) return;
            YapsSquashPreview.HandleGUI();
        }
    }

    // Writes the slots and the hand globals itself while on. Nothing it writes is saved: the
    // arrays are not serialised, and Stop puts them back.
    static class YapsSquashPreview
    {
        public static YapsSquashSettings On { get; private set; }
        public static float Squeeze;
        public static bool Bounce, Hand;
        static Vector3 _wrist;
        static Vector4 _params;
        static readonly Matrix4x4[] Empty = new Matrix4x4[20];

        public static void Start(YapsSquashSettings s)
        {
            Stop();
            var slots = YapsSquash.Slots(s.transform);
            if (slots == null) return;
            On = s;
            _params = Shader.GetGlobalVector("CVRGlobalParams1");
            // In front of the first part, a hand's length and a palm out.
            var part = slots[0] != null ? slots[0].position : s.transform.position + Vector3.up;
            _wrist = part + s.transform.forward * 0.25f;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Leave;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        static void Leave(PlayModeStateChange _) => Stop();

        public static void Stop()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= Leave;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            if (On == null) return;
            foreach (var m in YapsSquash.Materials(On.transform)) m.SetMatrixArray("_YAPS_SquashFollow", Empty);
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

            // Player 1's open left hand, reaching from a chest behind it toward the first part.
            var left = new Vector4[255];
            var chests = new Vector4[255];
            var roots = new Vector4[255];
            var aim = ((Vector3) m[0].GetColumn(3) - _wrist).normalized;
            var chest = _wrist - aim * 0.45f;
            left[1] = new Vector4(_wrist.x, _wrist.y, _wrist.z, -1f);
            chests[1] = chest;
            roots[1] = new Vector4(chest.x, 0f, chest.z, 1.7f);
            Shader.SetGlobalVectorArray("_CVR_PlayerLeftHandPositions", left);
            Shader.SetGlobalVectorArray("_CVR_PlayerRightHandPositions", new Vector4[255]);
            Shader.SetGlobalVectorArray("_CVR_PlayerChestPositions", chests);
            Shader.SetGlobalVectorArray("_CVR_PlayerRootPositions", roots);
            Shader.SetGlobalVector("CVRGlobalParams1", new Vector4(_params.x, Hand ? 2f : 0f, _params.z, _params.w));
            SceneView.RepaintAll();
        }

        // The wrist, and the palm the shader presses with drawn ahead of it.
        public static void HandleGUI()
        {
            var slots = YapsSquash.Slots(On.transform);
            if (slots == null || slots[0] == null) return;
            var aim = (slots[0].position - _wrist).normalized;
            float radius = On.handSize * 1.7f / 1.6f;
            Handles.color = YapsInspectorStyle.PlugColour;
            Handles.DrawWireDisc(_wrist + aim * 0.06f, aim, radius);
            Handles.DrawLine(_wrist, _wrist + aim * (0.11f * 1.7f));
            _wrist = Handles.PositionHandle(_wrist, Quaternion.LookRotation(aim));
        }
    }
}
#endif
