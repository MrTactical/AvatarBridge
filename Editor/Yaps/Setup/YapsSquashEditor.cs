// The YAPS Squash component's inspector, in steps: the parts with the region painter, the knobs
// (written straight to the build's materials) and the build last. Then the preview that presses
// the parts as the game does, run from the Avatar Tester alone: a second copy of its controls here
// read as a second preview. Another player's hands, written into the client's own globals, squeeze
// the pair from outside and press where dragged, in edit and Play mode alike. Edit mode runs no
// cloth, so there the preview also swings the rests under it.
#if CVR_CCK_EXISTS
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.LowLevel;
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
            { "Live tissue", "a twitch toggle, a grip that slides, and what it tells the animator" },
        };

        // Inside each fold: what it is and how to see it, which the header alone never said.
        static readonly System.Collections.Generic.Dictionary<string, string> FoldHints = new System.Collections.Generic.Dictionary<string, string>
        {
            { "How it gives", "How a part answers any press: how flat it goes and how wide it spreads to keep its volume. See it in the Avatar Tester with Squeeze or the test hand." },
            { "The pair", "With Meet on, a part swung or pushed into a still partner keeps its round front and pushes the partner aside by as much as it pushed in, the partner taking a rounded hollow where they still meet. Pushed alike from both sides, the two press flat against each other, swelling out round the contact. With Meet off, what overlaps passes through. See it in the Avatar Tester: Squeeze presses from both sides, and in Play mode Push one in shoves one part into the other." },
            { "In motion", "As the physics swing a part, it stretches away from where it hangs and squashes back. See it in the Avatar Tester with Bounce, or move the avatar in Play mode." },
            { "What presses it", "What dents the parts besides each other, each with its own strength. See hands in the Avatar Tester with the test hand." },
            { "Live tissue", "Twitch adds a menu toggle whose every flip kicks the jiggle; Grip slides lets a hand that grabs the skin stroke along it, the grip going with the hand. " +
                             "Put the jelly on again to change either. " +
                             "Whatever is set here, the jelly tells the animator three floats from 0 to 1 a pair, under the pair's name, for blend shapes, particles or anything else to follow: " +
                             "#Jelly/<pair>/Squeeze, how hard it is pressed or pulled; #Jelly/<pair>/Grab, 1 while a hand grips it; #Jelly/<pair>/Slap, how hard the last slap landed, fading. " +
                             "Each viewer works them out on their own machine, so they never sync." },
        };

        static readonly string[] HereNotKnobs = { "partBones", "leaveAlone" };
        static readonly string[] JellyOnly = { "twitch", "twitchSize", "gripSlides" };

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
            bool painted = settings.pairs.Count > 0;
            // One line for the build's state, the same one every window shows, naming what a
            // rebuild is for. A bare "built" left nobody knowing when to press it.
            string status = YapsSquash.Status(root, out var statusTone);
            bool needed = !built || statusTone == Tone.Warn;

            _root.Add(BridgeElements.Banner("Squash",
                !built ? "not built yet: work down the steps, then Build at the bottom"
                : needed ? "needs a rebuild: the last step says why" : "up to date",
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
            // Always open, so the step shows what squashes, coloured on the body, before anything
            // is pressed. Behind a "Paint them" button it showed nothing until painting began.
            if (_painter == null) _painter = YapsSquashPainter.For(root.gameObject, RebuildLater);
            if (painted)
                parts.Body.Add(BridgeElements.Hint($"Squashing {string.Join(" and ", settings.pairs.Select(p => p.name))}, painted on \"{(settings.body != null ? settings.body.name : "?")}\"."));

            // The painter lives here, not in a window of its own: a step that sent the reader to
            // another window was a step they lost.
            if (picked || _pickBones) _painter.Panel(parts.Body);
            else _painter.Panel(parts.Body, BridgeElements.Btn("Pick bones instead", () => { _pickBones = true; RebuildLater(); },
                "Pick by hand the bones of anything that comes in two: cheeks, thighs."));

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

            // The knobs here, the testing in the Avatar Tester: a knob shows nothing until something
            // presses, so the way there comes first in the card.
            var feel = new BridgeElements.Card("Tune how it feels").Step(2, 3, BridgeTheme.Span.Cvr);
            body.Add(feel);
            bool testing = YapsSquashPreview.On == settings;
            if (Application.isPlaying) feel.Body.Add(BridgeElements.Notice(Tone.Info, "Knobs moved in Play mode go back when it ends, as every component's do."));
            var test = BridgeElements.Btn(testing ? "Show the Avatar Tester" : "Test in the Avatar Tester", () =>
            {
                YapsSquashPreview.Test(settings);
                RebuildLater();
            }, built ? "Opens the Avatar Tester on this avatar with the squash preview running: Squeeze, Bounce and the test hand press it there." : "Build first");
            test.SetEnabled(built);
            feel.Body.Add(BridgeElements.ButtonRow(test));
            feel.Body.Add(BridgeElements.Hint(testing
                ? "Knobs write to the materials as you move them, no rebuild, and show on the Avatar Tester's preview."
                : built ? "Knobs write to the materials as you move them, no rebuild. Test them in the Avatar Tester to see them."
                : "Knobs set now are used by the build. Build in step 3, then test it in the Avatar Tester."));

            // Fields in declaration order, a fold per [Header]. The regions are hidden.
            var it = serializedObject.GetIterator();
            it.NextVisible(true);   // m_Script
            BridgeElements.Card fold = null;
            bool jellied = settings.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Jelly Region"));
            while (it.NextVisible(false))
            {
                if (HereNotKnobs.Contains(it.name) || !jellied && JellyOnly.Contains(it.name)) continue;
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
            if (built) build.Body.Add(BridgeElements.Notice(statusTone, status));
            build.Body.Add(BridgeElements.Hint(!built
                ? "Measures the parts and patches every layer over them. Shaders already carrying YAPS keep theirs."
                : "A rebuild is needed after step 1 changes, the body or its bones change, an outfit is added, or YAPS is updated. The line above says when; a preview running in the Avatar Tester carries on after it."));
            void Run()
            {
                // The build swaps the materials the preview writes to, so it stops and starts again.
                bool was = YapsSquashPreview.On == settings;
                YapsSquashPreview.Stop();
                if (_painter != null && _painter.IsPainting) _painter.SetPainting(false);
                _said = YapsSquash.Build(root, out bool ok);
                if (ok && was) YapsSquashPreview.Start(settings);
                else if (ok) _said += " Test it in the Avatar Tester from step 2.";
                _saidTone = ok ? Tone.Good : Tone.Warn;
                RebuildLater();
            }
            // Standing out only when it is needed: always primary, it read as a step left undone.
            if (needed) build.Body.Add(new BridgeElements.PrimaryButton(built ? "Rebuild the squash" : "Build the squash", Run, BridgeTheme.BridgeTo));
            else build.Body.Add(BridgeElements.ButtonRow(BridgeElements.Btn("Rebuild the squash", Run, "Up to date, so nothing changes unless something above did.")));
            if (_said != null) build.Body.Add(BridgeElements.Notice(_saidTone, _said));
            // Also a build whose objects were deleted by hand, its copies still worn.
            if (built || YapsSquash.Worn(root) > 0)
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
        public static float Squeeze, Push;
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

        // The preview's controls, for the Avatar Tester's squash section.
        // Returns the Preview button's row, so a caller can put its own buttons beside it.
        public static VisualElement Controls(VisualElement into, YapsSquashSettings settings, System.Action refresh)
        {
            bool built = YapsSquash.Has(YapsSquash.RootOf(settings));
            into.Add(Application.isPlaying
                ? BridgeElements.Hint("The cloth is real in Play mode: move the avatar (Jump or Shake in the Avatar Tester) and it swings and stretches. Push one in shoves one part of the first pair into the other; Squeeze and the test hand press it as another player's hands would.",
                    "Drag the hand with its handle in the scene view; it moves with the avatar. Pause Play mode to hold the cloth still while it presses. " +
                    "The Move tool cannot push a part in Play mode: the cloth puts its bones back every frame.")
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
            if (Application.isPlaying)
            {
                var push = new Slider("Push one in", 0f, 1f) { value = Push, showInputField = true,
                    tooltip = "Moves one part of the first pair toward its partner by its bone, at 1 halfway across the gap between them, as a swing or a shove would. " +
                              "The partner gives way, rounded; pushed from both sides, with Squeeze, the two press flat." };
                push.AddToClassList("ab-slider");
                push.RegisterValueChangedCallback(e => Push = e.newValue);
                into.Add(push);
            }
            if (!Application.isPlaying)
                into.Add(BridgeElements.Bind("Bounce", "Swings each part up and down under it, as a jump would, to show the stretch in motion.",
                    Bounce, b => Bounce = b));
            into.Add(BridgeElements.Bind("Test hand", "Another player's open hand, dragged with the handle in the scene view. It presses the part nearest it.",
                Hand, b => { Hand = b; SceneView.RepaintAll(); }));
            into.Add(BridgeElements.SliderField("Player height", "How tall the hand's player is, in metres. A hand is sized to its player, so lower it for a smaller hand pressing a smaller spot.",
                0.3f, 2.5f, Tall, v => { Tall = v; SceneView.RepaintAll(); }));
            return row;
        }

        // The Avatar Tester on this avatar, its squash section open and the preview running. The
        // tester ships in the public package and the add-on can be installed alone, so it is
        // reached by name.
        public static void Test(YapsSquashSettings s)
        {
            var open = typeof(YapsSquashPreview).Assembly.GetType("AvatarBridge.CckAnimatorTester")
                ?.GetMethod("OpenFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (open == null)
            {
                Debug.LogError("[YAPS] The Avatar Tester has no OpenFor, so the squash cannot be tested from here. Open it from Tools > Avatar Bridge.");
                return;
            }
            EditorPrefs.SetBool("AvatarBridge.Card.Tester.Physics", true);
            EditorPrefs.SetBool("AvatarBridge.Card.Tester.Squash", true);
            if (On != s) Start(s);
            open.Invoke(null, new object[] { YapsSquash.RootOf(s).GetComponent<ABI.CCK.Components.CVRAvatar>() });
        }

        // Knobs moved in Play mode stay on the materials, which are assets, while the component
        // goes back to its edit mode values when Play ends: the two then disagreed in silence.
        [InitializeOnLoadMethod]
        static void KeepKnobs() => EditorApplication.playModeStateChanged += change =>
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            foreach (var s in Object.FindObjectsOfType<YapsSquashSettings>(true)) YapsSquash.Apply(YapsSquash.RootOf(s));
        };

        public static void Start(YapsSquashSettings s)
        {
            Stop();
            var root = YapsSquash.RootOf(s);
            var slots = YapsSquash.Slots(root);
            if (slots == null) return;
            On = s;
            _root = root;
            // Pressed from the start, so a knob shows as soon as it moves. Not in Play mode, where
            // the cloth moves on its own and a push from one side read as flat under the squeeze.
            Squeeze = Application.isPlaying ? 0f : 0.5f;
            _params = Shader.GetGlobalVector("CVRGlobalParams1");
            // In front of the first part, a hand's length and a palm out.
            var part = slots[0] != null ? slots[0].position : root.position + Vector3.up;
            _wrist = root.InverseTransformPoint(part + root.forward * 0.25f);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Leave;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            // Here and not in an inspector, so the handle stays when the tester runs it.
            SceneView.duringSceneGui += Draw;
            Hook(true);
        }

        static void Leave(PlayModeStateChange _) => Stop();

        public static void Stop()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= Leave;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            SceneView.duringSceneGui -= Draw;
            Hook(false);
            if (On == null) return;
            Push = 0f;
            PushNow();
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

        // Push one in runs at the start of each frame's Update: after the cloth has put its bones
        // back to their pose, before the animator and the cloth read them. Set between frames, as
        // the Move tool sets them, the cloth undid it before it read them (measured).
        struct PushStep { }
        static Transform _pushed;
        static Vector3 _pushedFrom, _pushBy;

        static void Hook(bool on)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;
                var list = loop.subSystemList[i].subSystemList.Where(x => x.type != typeof(PushStep)).ToList();
                if (on) list.Insert(0, new PlayerLoopSystem { type = typeof(PushStep), updateDelegate = PushNow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        // The first pair's first part, moved by its chain's root, the bone the cloth holds still
        // and carries the chain with: the first under the rest's parent on the way up from the
        // follow. One pair only: every pair at once shoved a second one unasked. The push is kept
        // in the root's parent's space, so it turns with the avatar.
        static void PushNow()
        {
            if (!Application.isPlaying) return;
            var slots = _root != null ? YapsSquash.Slots(_root) : null;
            if (Push <= 0f || slots == null || slots[0] == null || slots[1] == null || slots[4] == null)
            {
                // Put back, for a bone no cloth restores.
                if (_pushed != null) _pushed.localPosition = _pushedFrom;
                _pushed = null;
                return;
            }
            if (_pushed == null)
            {
                var bone = slots[0];
                while (bone.parent != null && bone.parent != slots[4].parent) bone = bone.parent;
                bone = bone == slots[0] || bone.parent == null ? slots[0].parent : bone;
                if (bone == null || bone.parent == null) return;
                _pushed = bone;
                _pushedFrom = bone.localPosition;
                _pushBy = bone.parent.InverseTransformVector((slots[1].position - slots[0].position) * 0.5f);
            }
            _pushed.localPosition = _pushedFrom + _pushBy * Push;
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
