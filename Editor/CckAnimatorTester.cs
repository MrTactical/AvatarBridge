#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Play-mode driver for everything CVR feeds an avatar: gestures,
    // locomotion, visemes, emotes, the Advanced Settings menu.
    // Gesture Manager cannot test conversions; it needs the removed
    // VRC descriptor. Every control here writes the parameters the
    // client writes, coerced by declared type like the client.
    public partial class CckAnimatorTester : EditorWindow
    {
        // The physics card lives in its own file, each solver it reads behind
        // that solver's own define.
        partial void EnablePhysics();
        partial void DisablePhysics();
        partial void PhysicsPlayModeChanged(PlayModeStateChange change);
        partial void PhysicsSelectionChanged();
        partial void AddPhysicsCard(VisualElement scroll, CVRAvatar avatar, bool live);

        [MenuItem("Tools/Avatar Bridge/CCK Animator Tester")]
        static void Open()
        {
            var window = GetWindow<CckAnimatorTester>();
            window.titleContent = new GUIContent("CCK Animator Tester");
            window.minSize = new Vector2(360, 420);
        }

        // From the Toolkit, already pointed at its avatar.
        static void OpenFor(CVRAvatar avatar)
        {
            Open();
            var window = GetWindow<CckAnimatorTester>();
            window._override = avatar;
            window.Rebuild();
        }

        static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
            "nn", "RR", "aa", "E", "I", "O", "U"
        };

        static readonly (string name, int value, string tip)[] Poses =
        {
            ("Idle", 0, null), ("Open", -1, null), ("Fist", 1, null), ("Thumbs", 2, null),
            ("Gun", 3, null), ("Point", 4, null), ("Peace", 5, null), ("RnR", 6, "Rock'n'roll")
        };

        // The state flags are not independent; the client computes them together. Chairs and
        // water keep Grounded true, crouch and prone derive from Upright in VR, Flying
        // interrupts everything. So: one exclusive stance writing the exact flag set the game
        // would feed, never a flag soup.
        static readonly (string name, string tip)[] Stances =
        {
            ("Standing", "Grounded, nothing else: Standard Locomotion."),
            ("Crouching", "Crouching + Grounded, Upright into the crouch band (0.40–0.75). " +
                          "In VR the game derives this from your real height."),
            ("Prone", "Prone + Grounded, Upright below 0.40: the game's prone threshold."),
            ("Airborne", "Grounded off, nothing else: the jump/fall chain " +
                         "(JumpStart, JumpAir, then JumpLand when Grounded returns)."),
            ("Flying", "Flying on, Grounded off. An AnyState override in the CCK layer: " +
                       "it interrupts every state, emotes included."),
            ("Sitting", "Sitting + Grounded: the game keeps Grounded true in chairs."),
            ("Swimming", "Swimming + Grounded: the game keeps Grounded true in water too."),
        };

        static readonly string[] StanceFlags = { "Grounded", "Crouching", "Prone", "Flying", "Sitting", "Swimming" };

        CVRAvatar _override;
        float _loudness = 1f;
        int _fingerprint;
        double _nextPoll;

        // What the face card asks for, and the mesh indices to write
        // it through. Held every frame, not written once; the client
        // holds these weights too, and a single write loses to the
        // next animator evaluation. Indices cached like the client
        // caches them; this runs on the render path.
        SkinnedMeshRenderer _faceMesh;
        int[] _blinkIndexes = new int[0];
        int[] _visemeIndexes = new int[0];
        bool _blinkEnabled;
        bool _visemesEnabled;
        float _blink;
        int _viseme;

        // Several cards write the same parameters: Reset, the physics motions and the stance
        // all move what the sliders show. Each control registers how to show a value, and Drive
        // calls them after writing, so no card is left showing a value the animator dropped.
        readonly Dictionary<string, List<System.Action<float>>> _views =
            new Dictionary<string, List<System.Action<float>>>();

        void View(string parameter, System.Action<float> show)
        {
            if (!_views.TryGetValue(parameter, out var list))
            {
                _views[parameter] = list = new List<System.Action<float>>();
            }
            list.Add(show);
        }

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Selection.selectionChanged += OnSelectionChanged;
            // The menu card mirrors the Animator's current controller.
            // The CCK regenerates it and conversions swap it wholesale;
            // polling a cheap fingerprint keeps the card true.
            EditorApplication.update += PollForChanges;
            // onBeforeRender and not EditorApplication.update: it fires inside the player loop
            // after LateUpdate and before anything is drawn, which is exactly where the client
            // writes these weights. An editor-update write lands at an unspecified point around
            // the frame and loses to the animator often enough to look broken.
            Application.onBeforeRender += HoldFaceShapes;
            EnablePhysics();
        }

        void CreateGUI()
        {
            // Skin and stylesheet applied here and never again, bar OnFocus. isProSkin is not
            // dependable inside update polls, and one false reading would stick. Rebuild only
            // clears children, so this setup survives every rebuild.
            BridgeElements.Root(rootVisualElement);
            Rebuild();
        }

        void OnFocus()
        {
            if (rootVisualElement != null)
            {
                BridgeTheme.ApplySkin(rootVisualElement);
            }
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= PollForChanges;
            Application.onBeforeRender -= HoldFaceShapes;
            DisablePhysics();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            PhysicsPlayModeChanged(change);
            Rebuild();
        }

        // A click inside the same avatar resolves to it again, and a rebuild
        // would reset the face it is holding and rescan every blend tree.
        void OnSelectionChanged()
        {
            if (ComputeFingerprint(ResolveAvatar()) != _fingerprint)
            {
                Rebuild();
            }
            else
            {
                PhysicsSelectionChanged();
            }
        }

        void PollForChanges()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
            {
                return;
            }
            _nextPoll = EditorApplication.timeSinceStartup + 0.5;
            // Twice a second, not per frame: the Eye Blink and viseme fields are edited in the
            // inspector while the tester is open, and the fingerprint below deliberately does not
            // cover them.
            // Resolved once for both: with nothing selected it searches the scene.
            var avatar = ResolveAvatar();
            CacheFaceShapes(avatar);
            if (ComputeFingerprint(avatar) != _fingerprint)
            {
                Rebuild();
            }
        }

        int ComputeFingerprint(CVRAvatar avatar)
        {
            unchecked
            {
                int hash = avatar != null ? avatar.GetInstanceID() : 0;
                var animator = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
                var controller = animator != null ? animator.runtimeAnimatorController : null;
                hash = hash * 31 + (controller != null ? controller.GetInstanceID() : 0);
                foreach (var name in ControllerParameterList(avatar))
                {
                    hash = hash * 31 + name.GetHashCode();
                }
                var settings = avatar != null && avatar.avatarSettings != null
                    ? avatar.avatarSettings.settings
                    : null;
                if (settings != null)
                {
                    foreach (var entry in settings)
                    {
                        if (entry == null)
                        {
                            continue;
                        }
                        hash = hash * 31 + (entry.machineName ?? "").GetHashCode();
                        hash = hash * 31 + (int)entry.type;
                    }
                }
                return hash;
            }
        }

        static List<string> ControllerParameterList(CVRAvatar avatar)
        {
            var names = new List<string>();
            var animator = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            if (runtime is UnityEditor.Animations.AnimatorController editable)
            {
                foreach (var parameter in editable.parameters)
                {
                    names.Add(parameter.name);
                }
            }
            return names;
        }

        CVRAvatar ResolveAvatar()
        {
            if (_override != null)
            {
                return _override;
            }
            var selected = Selection.activeGameObject;
            var fromSelection = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (fromSelection != null)
            {
                return fromSelection;
            }
            // A conversion scene usually holds several CVRAvatars.
            // Prefer an active one whose animator has a controller.
            CVRAvatar best = null;
            int bestScore = -1;
            foreach (var candidate in FindObjectsOfType<CVRAvatar>(true))
            {
                var animator = candidate.GetComponentInChildren<Animator>(true);
                bool usable = animator != null && animator.runtimeAnimatorController != null;
                bool active = candidate.gameObject.activeInHierarchy;
                int score = (usable ? 2 : 0) + (active ? 1 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        Animator LiveAnimator()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AvatarBridge] The tester needs play mode: animators only evaluate there.");
                return null;
            }
            var avatar = ResolveAvatar();
            if (avatar == null)
            {
                Debug.LogWarning("[AvatarBridge] No CVRAvatar component found anywhere in the scene.");
                return null;
            }
            var animator = avatar.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning($"[AvatarBridge] \"{avatar.name}\" has no Animator component anywhere under it.");
                return null;
            }
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[AvatarBridge] \"{avatar.name}\"'s Animator has no controller assigned: " +
                                 "was this conversion finished, or the assignment lost with an unsaved scene?");
                return null;
            }
            return animator;
        }

        // animator.parameters copies every parameter on each read, and a dragged
        // slider drives once per event. Read again for another animator or
        // controller, a changed count (an animator that only now initialised),
        // and after every rebuild.
        static Animator _typesFor;
        static RuntimeAnimatorController _typesController;
        static int _typesCount;
        static readonly Dictionary<string, AnimatorControllerParameterType> _types =
            new Dictionary<string, AnimatorControllerParameterType>();

        static bool TryParameterType(Animator animator, string name, out AnimatorControllerParameterType type)
        {
            type = default;
            if (animator == null || name == null)
            {
                return false;
            }
            if (animator != _typesFor || animator.runtimeAnimatorController != _typesController
                || animator.parameterCount != _typesCount)
            {
                _types.Clear();
                foreach (var parameter in animator.parameters)
                {
                    // First wins, as the linear search it replaces did.
                    if (!_types.ContainsKey(parameter.name))
                    {
                        _types.Add(parameter.name, parameter.type);
                    }
                }
                _typesFor = animator;
                _typesController = animator.runtimeAnimatorController;
                _typesCount = animator.parameterCount;
            }
            return _types.TryGetValue(name, out type);
        }

        void Drive(Animator animator, string name, float value)
        {
            if (!TryParameterType(animator, name, out var type))
            {
                return;
            }
            switch (type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(name, value);
                    break;
                case AnimatorControllerParameterType.Int:
                    value = Mathf.RoundToInt(value);
                    animator.SetInteger(name, (int)value);
                    break;
                case AnimatorControllerParameterType.Bool:
                    value = value != 0f ? 1f : 0f;
                    animator.SetBool(name, value != 0f);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    if (value != 0f)
                    {
                        animator.SetTrigger(name);
                    }
                    return;
            }
            if (_views.TryGetValue(name, out var views))
            {
                foreach (var show in views)
                {
                    show(value);
                }
            }
        }

        void Rebuild()
        {
            // Stored up front so the poll doesn't immediately rebuild what was just built.
            var avatar = ResolveAvatar();
            _fingerprint = ComputeFingerprint(avatar);
            CacheFaceShapes(avatar);
            // A rebuilt controller can keep its object and change its parameters.
            _typesFor = null;
            _views.Clear();
            rootVisualElement.Clear();
            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                // A half-built window looks like a styling bug.
                // Say what happened, in the window itself.
                Debug.LogException(e);
                var failure = new BridgeElements.Card("The tester failed to build its UI");
                failure.Body.Add(BridgeElements.Hint(
                    $"{e.GetType().Name}: {e.Message}\n\nThe full stack trace is in the Console. " +
                    "This is a bug in AvatarBridge, not in your avatar: please report it with " +
                    "that message."));
                rootVisualElement.Add(failure);
            }
        }

        static VisualElement SafeCard(string what, System.Func<VisualElement> build)
        {
            try
            {
                return build();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                var card = new BridgeElements.Card($"{what}: failed to build");
                card.Body.Add(BridgeElements.Hint($"{e.GetType().Name}: {e.Message}"));
                return card;
            }
        }

        void Build()
        {
            var root = rootVisualElement;
            root.Add(BridgeElements.Banner("CCK Animator Tester",
                "drive a ChilloutVR avatar the way the game does", "v" + BridgeDefines.Version,
                BridgeTheme.Span.Cvr));

            var avatar = ResolveAvatar();
            bool live = Application.isPlaying && avatar != null;
            // Read for each control's starting value, so a rebuild in Play mode shows what the
            // animator holds instead of snapping every control back to zero.
            var quiet = live ? avatar.GetComponentInChildren<Animator>(true) : null;

            // Outside the scroll, so the reason the window is idle is always on screen.
            BridgeElements.NoticeBox state = null;
            if (avatar == null)
            {
                state = BridgeElements.Notice(Tone.Info,
                    "No ChilloutVR avatar found. Select one in the scene, or drop it in the Avatar card.");
            }
            else if (!Application.isPlaying)
            {
                state = BridgeElements.Notice(Tone.Info, $"Found \"{avatar.name}\". Enter Play mode to drive it.",
                    action: BridgeElements.Btn("Enter Play mode", EditorApplication.EnterPlaymode,
                        "Animators only evaluate in Play mode.", ButtonKind.Strong));
            }
            if (state != null)
            {
                state.AddToClassList("ab-pinned");
                root.Add(state);
            }

            var scroll = BridgeElements.Scroll();
            root.Add(scroll);
            scroll.Add(BuildAvatarCard(avatar, live));
            scroll.Add(BuildGesturesCard(quiet, live));
            scroll.Add(BuildLocomotionCard(quiet, live));
            // Beside the locomotion it drives.
            AddPhysicsCard(scroll, avatar, live);
            scroll.Add(BuildFaceCard(live));
            scroll.Add(SafeCard("Face tracking", () => BuildFaceTrackingCard(avatar, live)));

            var menu = new BridgeElements.Card("Avatar menu", "Advanced Avatar Settings", expanded: true)
                .Remember("Tester.Avatar menu");
            BuildMenuControls(menu, avatar, live);
            scroll.Add(menu);

            scroll.Add(BuildRemoteCard(live));

            scroll.Add(BridgeElements.Footer(
                BridgeElements.ExternalLink("Guide", BridgeLinks.Repo + "#highlights",
                    "What the tester does, in the README"),
                // A link that builds its url from this install, so it cannot be a plain ExternalLink.
                BridgeElements.TextLink("Report an issue ↗", () => BridgeLinks.OpenBugReport(),
                    "Opens a GitHub issue with your Unity and package versions filled in")));

            // Pinned below the scroll view, not inside it. The point is watching layers react
            // while a control is driven, so the readout must stay on screen. Its own rows scroll
            // internally, so fifty layers cannot swallow the window either.
            root.Add(SafeCard("Animator layers", () => BuildLayerCard(avatar)));
        }

        VisualElement BuildAvatarCard(CVRAvatar avatar, bool live)
        {
            var card = new BridgeElements.Card("Avatar", expanded: true).Remember("Tester.Avatar");
            card.Body.Add(BridgeElements.ObjectPicker<CVRAvatar>("CVR avatar", _override, picked =>
            {
                _override = picked;
                Rebuild();
            }, "Pin an avatar here, or leave it empty to follow the scene selection."));
            if (_override != null)
            {
                var pinned = BridgeElements.Hint("Pinned: scene selection is ignored.");
                pinned.AddToClassList("ab-grow");
                // The rebuild clears this link, so it waits out its own click.
                Button unpin = null;
                unpin = BridgeElements.TextLink("Unpin", () => BridgeElements.Defer(unpin, () =>
                {
                    _override = null;
                    Rebuild();
                }), "Follow the scene selection again");
                card.Body.Add(BridgeElements.Row(pinned, unpin));
            }
            else if (avatar != null)
            {
                card.Body.Add(BridgeElements.Hint($"Using \"{avatar.name}\" from the scene. Drop one here to pin it."));
            }
            var resting = BridgeElements.Btn("Resting defaults", RestingDefaults,
                "Back to what the game reports standing still");
            resting.SetEnabled(live);
            card.Body.Add(BridgeElements.ButtonRow(resting));
            return card;
        }

        // The values the game itself rests at. Standing writes the whole stance flag set and
        // returns Upright to 1; every card showing one of these follows through its view.
        void RestingDefaults()
        {
            var a = LiveAnimator();
            if (a == null)
            {
                return;
            }
            DriveStance("Standing", moveUpright: true);
            Drive(a, "AFK", 0f);
            Drive(a, "IsLocal", 1f);
            Drive(a, "TrackingType", 3f);
            Drive(a, "VRMode", 0f);
            Drive(a, "MovementX", 0f);
            Drive(a, "MovementY", 0f);
            Drive(a, "VelocityX", 0f);
            Drive(a, "VelocityZ", 0f);
            Drive(a, "GestureLeftIdx", 0f);
            Drive(a, "GestureRightIdx", 0f);
            Drive(a, "GestureLeft", 0f);
            Drive(a, "GestureRight", 0f);
        }

        VisualElement BuildGesturesCard(Animator quiet, bool live)
        {
            var card = new BridgeElements.Card("Gestures", expanded: true).Remember("Tester.Gestures");
            card.Body.Add(BridgeElements.Caption("Left hand"));
            card.Body.Add(PoseSegment("GestureLeft", quiet, live));
            card.Body.Add(BridgeElements.Caption("Right hand"));
            card.Body.Add(PoseSegment("GestureRight", quiet, live));
            card.Body.Add(TriggerSlider("Left trigger", "GestureLeft", quiet, live));
            card.Body.Add(TriggerSlider("Right trigger", "GestureRight", quiet, live));
            return card;
        }

        VisualElement BuildLocomotionCard(Animator quiet, bool live)
        {
            var card = new BridgeElements.Card("Locomotion", expanded: true).Remember("Tester.Locomotion");

            var strafe = BridgeElements.SliderField("Strafe", "Movement X, with Velocity X at about run speed",
                -1f, 1f, ReadParam(quiet, "MovementX") ?? 0f, v =>
                {
                    var a = LiveAnimator();
                    Drive(a, "MovementX", v);
                    Drive(a, "VelocityX", v * 4f); // ~run speed, so velocity-driven trees react too
                });
            View("MovementX", strafe.SetValueWithoutNotify);
            var forward = BridgeElements.SliderField("Forward", "Movement Y, with Velocity Z at about run speed",
                -1f, 1f, ReadParam(quiet, "MovementY") ?? 0f, v =>
                {
                    var a = LiveAnimator();
                    Drive(a, "MovementY", v);
                    Drive(a, "VelocityZ", v * 4f);
                });
            View("MovementY", forward.SetValueWithoutNotify);

            // Each flag as last written, so the lit stance is the one the layer would pick.
            var flags = StanceFlags.ToDictionary(f => f, f => ReadParam(quiet, f));
            var stance = new BridgeElements.Segmented(Stances.Select(s => s.name).ToArray(), StanceFrom(flags),
                i => DriveStance(Stances[i].name, moveUpright: true), Stances.Select(s => s.tip).ToArray());
            foreach (string flag in StanceFlags)
            {
                string captured = flag;
                View(flag, v =>
                {
                    flags[captured] = v;
                    stance.SetCurrent(StanceFrom(flags));
                });
            }

            var upright = BridgeElements.SliderField("Upright",
                "1 = standing. Viewpoint over avatar height: below 0.75 crouches, below 0.40 goes prone, as in game.",
                0f, 1f, ReadParam(quiet, "Upright") ?? 1f, v =>
                {
                    Drive(LiveAnimator(), "Upright", v);
                    // Mirror the client's VR derivation, ground stances only.
                    // CanCrouch/CanProne refuse while flying, swimming, sitting.
                    string current = Stances[StanceFrom(flags)].name;
                    if (current == "Standing" || current == "Crouching" || current == "Prone")
                    {
                        string derived = v <= 0.4f ? "Prone" : v <= 0.75f ? "Crouching" : "Standing";
                        if (derived != current)
                        {
                            DriveStance(derived, moveUpright: false);
                        }
                    }
                });
            View("Upright", upright.SetValueWithoutNotify);

            // AFK is fed from the headset proximity sensor. Only
            // reaches avatars that declare an AFK parameter.
            var afk = BridgeElements.Bind("AFK",
                "The headset proximity sensor in game. Independent of stance; only does anything if the " +
                "avatar declares an AFK parameter.",
                (ReadParam(quiet, "AFK") ?? 0f) > 0.5f, on => Drive(LiveAnimator(), "AFK", on ? 1f : 0f));
            View("AFK", v => afk.SetValueWithoutNotify(v > 0.5f));

            foreach (var control in new VisualElement[] { strafe, forward, stance, upright, afk })
            {
                control.SetEnabled(live);
            }
            card.Body.Add(strafe);
            card.Body.Add(forward);
            card.Body.Add(BridgeElements.Caption("Stance"));
            card.Body.Add(stance);
            card.Body.Add(upright);
            card.Body.Add(afk);
            return card;
        }

        static int StanceFrom(Dictionary<string, float?> flags)
        {
            bool On(string name) => (flags[name] ?? 0f) > 0.5f;
            // The layer's own precedence.
            string stance =
                On("Flying") ? "Flying" :
                On("Sitting") ? "Sitting" :
                On("Swimming") ? "Swimming" :
                On("Prone") ? "Prone" :
                On("Crouching") ? "Crouching" :
                (flags["Grounded"] ?? 1f) < 0.5f ? "Airborne" : "Standing";
            return System.Array.FindIndex(Stances, s => s.name == stance);
        }

        void DriveStance(string name, bool moveUpright)
        {
            var a = LiveAnimator();
            Drive(a, "Grounded", name == "Airborne" || name == "Flying" ? 0f : 1f);
            Drive(a, "Crouching", name == "Crouching" ? 1f : 0f);
            Drive(a, "Prone", name == "Prone" ? 1f : 0f);
            Drive(a, "Flying", name == "Flying" ? 1f : 0f);
            Drive(a, "Sitting", name == "Sitting" ? 1f : 0f);
            Drive(a, "Swimming", name == "Swimming" ? 1f : 0f);
            // Ground stances drag Upright along, mirroring the VR height derivation.
            float height = name == "Standing" ? 1f
                : name == "Crouching" ? 0.6f
                : name == "Prone" ? 0.25f : -1f;
            if (moveUpright && height >= 0f)
            {
                Drive(a, "Upright", height);
            }
        }

        VisualElement BuildFaceCard(bool live)
        {
            // Visemes and blinking are not animator features in CVR;
            // the client writes blendshape weights on the mesh
            // directly. Parameters are still driven for animator logic,
            // but the visible mouth comes from the blendshapes.
            var card = new BridgeElements.Card("Face & emotes", expanded: true).Remember("Tester.Face & emotes");
            // Held on the mesh every frame; backing fields must start
            // where the controls start.
            _viseme = 0;
            _loudness = 1f;
            _blink = 0f;
            var viseme = BridgeElements.Popup("Viseme", "The mouth shape lipsync reports", VisemeNames, 0,
                index => ApplyViseme(index, _loudness));
            var loudness = BridgeElements.SliderField("Viseme loudness", "How far the viseme opens", 0f, 1f, 1f,
                v => ApplyViseme(viseme.index, v));
            var blink = BridgeElements.SliderField("Blink", "0 open, 1 shut", 0f, 1f, 0f, ApplyBlink);
            int emote = 0;
            var emoteField = BridgeElements.IntField("Emote", "The emote number the game plays", 0, v => emote = v);
            emoteField.AddToClassList("ab-grow");
            var play = BridgeElements.Btn("Play", () => Drive(LiveAnimator(), "Emote", emote), "Play this emote");
            var cancel = BridgeElements.Btn("Cancel", () =>
            {
                var a = LiveAnimator();
                Drive(a, "Emote", 0f);
                Drive(a, "CancelEmote", 1f);
            }, "Cancel the playing emote");
            var emoteRow = BridgeElements.Row(emoteField, play, cancel);
            // The controls, not the row: a disabled row fades and its buttons fade again inside it.
            foreach (var control in new VisualElement[] { viseme, loudness, blink, emoteField, play, cancel })
            {
                control.SetEnabled(live);
            }
            card.Body.Add(viseme);
            card.Body.Add(BridgeElements.Hint(
                "Visemes and blink are written after the animator, as in game, so they beat any animation on " +
                "the same shape."));
            card.Body.Add(loudness);
            card.Body.Add(blink);
            card.Body.Add(emoteRow);
            return card;
        }

        // The avatar as other players run it. Remote copies never receive "#" locals, and
        // streams are stripped from them, so those parameters sit at their defaults forever. An
        // animator depending on a live local value can behave differently for others: a layer
        // that looks parked to the wearer can cycle between states for everyone else, which in
        // game reads as an animation rapidly looping that the wearer cannot see at all.
        VisualElement BuildRemoteCard(bool live)
        {
            var card = new BridgeElements.Card("Remote view", expanded: true).Remember("Tester.Remote view");
            card.Body.Add(BridgeElements.Hint(
                "Puts every \"#\" local parameter at its default, as other players' clients hold it.",
                "A layer that then cycles or changes state does so for everyone but you."));
            var snap = BridgeElements.Btn("Snap \"#\" locals to their defaults", () =>
            {
                var a = LiveAnimator();
                if (a == null)
                {
                    return;
                }
                foreach (var parameter in a.parameters)
                {
                    if (!parameter.name.StartsWith("#"))
                    {
                        continue;
                    }
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            Drive(a, parameter.name, parameter.defaultBool ? 1f : 0f);
                            break;
                        case AnimatorControllerParameterType.Int:
                            Drive(a, parameter.name, parameter.defaultInt);
                            break;
                        case AnimatorControllerParameterType.Float:
                            Drive(a, parameter.name, parameter.defaultFloat);
                            break;
                    }
                }
            }, "How remote clients hold them");
            snap.SetEnabled(live);
            card.Body.Add(BridgeElements.ButtonRow(snap));
            return card;
        }

        void ApplyViseme(int index, float loudness)
        {
            var animator = LiveAnimator();
            Drive(animator, "VisemeIdx", index);
            Drive(animator, "VisemeLoudness", loudness);
            _viseme = index;
            _loudness = loudness;
            HoldFaceShapes();
        }

        void ApplyBlink(float amount)
        {
            _blink = amount;
            HoldFaceShapes();
        }

        void CacheFaceShapes(CVRAvatar avatar)
        {
            _faceMesh = avatar != null ? avatar.bodyMesh : null;
            var shared = _faceMesh != null ? _faceMesh.sharedMesh : null;
            if (shared == null)
            {
                _blinkIndexes = _visemeIndexes = new int[0];
                _blinkEnabled = _visemesEnabled = false;
                return;
            }
            _blinkEnabled = avatar.useBlinkBlendshapes;
            _visemesEnabled = avatar.useVisemeLipsync
                && avatar.visemeMode == CVRAvatar.CVRAvatarVisemeMode.Visemes;
            _blinkIndexes = Indices(shared, avatar.blinkBlendshape);
            _visemeIndexes = Indices(shared, avatar.visemeBlendshapes);
        }

        static int[] Indices(Mesh shared, string[] names)
        {
            if (names == null)
            {
                return new int[0];
            }
            var indices = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                indices[i] = string.IsNullOrEmpty(names[i]) ? -1 : shared.GetBlendShapeIndex(names[i]);
            }
            return indices;
        }

        void HoldFaceShapes()
        {
            if (!EditorApplication.isPlaying || _faceMesh == null)
            {
                return;
            }
            if (_blinkEnabled)
            {
                foreach (int shape in _blinkIndexes)
                {
                    if (shape >= 0)
                    {
                        _faceMesh.SetBlendShapeWeight(shape, _blink * 100f);
                    }
                }
            }
            if (_visemesEnabled)
            {
                for (int i = 0; i < _visemeIndexes.Length; i++)
                {
                    if (_visemeIndexes[i] >= 0)
                    {
                        _faceMesh.SetBlendShapeWeight(_visemeIndexes[i],
                            i == _viseme ? _loudness * 100f : 0f);
                    }
                }
            }
        }

        VisualElement PoseSegment(string floatParameter, Animator quiet, bool live)
        {
            var segment = new BridgeElements.Segmented(Poses.Select(p => p.name).ToArray(),
                // Idle outside Play mode, as the stance shows its resting Standing.
                PoseIndex(ReadParam(quiet, floatParameter) ?? 0f), i =>
                {
                    int value = Poses[i].value;
                    var animator = LiveAnimator();
                    // Int drives the pose states, float rides along,
                    // weight curls the analog fist. The client's trio.
                    Drive(animator, floatParameter + "Idx", value);
                    Drive(animator, floatParameter, value);
                    Drive(animator, floatParameter + "Weight", value == 1 ? 1f : 0f);
                }, Poses.Select(p => p.tip).ToArray());
            View(floatParameter, v => segment.SetCurrent(PoseIndex(v)));
            segment.SetEnabled(live);
            return segment;
        }

        // Anything in the fist band reads as the fist: the trigger squeeze is that gesture.
        static int PoseIndex(float? value)
        {
            if (value == null)
            {
                return -1;
            }
            float v = value.Value;
            int pose = v > 0f && v < 1f ? 1 : Mathf.RoundToInt(v);
            return System.Array.FindIndex(Poses, p => p.value == pose);
        }

        // The game's analog fist: the trigger squeeze is the gesture. GestureLeft carries the
        // 0..1 grip value in the fist band; driving only the weight does nothing.
        VisualElement TriggerSlider(string label, string floatParameter, Animator quiet, bool live)
        {
            float now = ReadParam(quiet, floatParameter) ?? 0f;
            var slider = BridgeElements.SliderField(label, "How far the fist curls", 0f, 1f,
                now >= 0f && now <= 1f ? now : 0f, v =>
                {
                    var a = LiveAnimator();
                    Drive(a, floatParameter, v);
                    Drive(a, floatParameter + "Idx", Mathf.RoundToInt(v));
                    Drive(a, floatParameter + "Weight", v);
                });
            // A pose past the fist band is not a squeeze.
            View(floatParameter, v => slider.SetValueWithoutNotify(v >= 0f && v <= 1f ? v : 0f));
            slider.SetEnabled(live);
            return slider;
        }

        VisualElement BuildLayerCard(CVRAvatar avatar)
        {
            // Collapsed by default and remembered. A diagnostic, not
            // something to scroll past on every visit.
            var card = new BridgeElements.Card("Animator layers", expanded: false).Remember("Tester.Layers");
            card.AddToClassList("ab-pinned");
            var animator = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            var asset = runtime as UnityEditor.Animations.AnimatorController;

            if (!Application.isPlaying || animator == null || asset == null)
            {
                card.SetSummary(Application.isPlaying ? "no controller" : "Play mode only");
                card.Body.Add(BridgeElements.Hint(
                    animator == null || asset == null
                        ? "No animator controller to read yet."
                        : "Enter Play mode to see layer weights and playing clips, as the CCK Debugger shows them."));
                return card;
            }

            // Which layers own the hand pose, and therefore which layers above them are able to
            // ruin it. Highest index wins: it is the one that writes last.
            int handTop = -1;
            for (int i = 0; i < asset.layers.Length; i++)
            {
                if (IsHandLayer(asset.layers[i].name))
                {
                    handTop = i;
                }
            }

            card.Body.Add(BridgeElements.Chips(
                BridgeElements.Chip("hand layer", Tone.Info, tooltip: "A layer that owns the hand pose"),
                BridgeElements.Chip("⚠ conflict", Tone.Bad,
                    tooltip: "Above the hand-pose layer, with a mask that lets it write finger muscles")));

            var header = new VisualElement();
            header.AddToClassList("ab-table-head");
            header.Add(Cell("#", width: 22));
            header.Add(Cell("Layer", share: 1f));
            header.Add(Cell("Weight", width: 44));
            // 88 fits "GesturesRight", and the hand masks are what this table exists to check.
            header.Add(Cell("Mask", width: 88));
            header.Add(Cell("Playing", share: 1.3f));
            var gutter = new VisualElement();
            header.Add(gutter);
            card.Body.Add(header);

            // The rows scroll under the fixed column header, capped so the pinned card leaves
            // the controls above it room to breathe. Scrollbar space is why the header sits
            // outside: with it inside, the header would shift left whenever the bar appears.
            var list = new ScrollView();
            list.AddToClassList("ab-list-short");
            card.Body.Add(list);
            // The rows lose the scrollbar's width and the header does not, so the flexible
            // columns would split the difference and every column after Layer drift right.
            // Measured, so it follows the bar appearing and going: a data width.
            list.contentViewport.RegisterCallback<GeometryChangedEvent>(_ =>
                gutter.style.width = list.layout.width - list.contentViewport.layout.width);

            var rows = new List<(Label weight, Label playing)>();
            int conflictCount = 0;
            for (int i = 0; i < animator.layerCount && i < asset.layers.Length; i++)
            {
                var layer = asset.layers[i];
                bool hand = IsHandLayer(layer.name);
                bool conflicts = i > handTop && handTop >= 0 && PermitsFingers(layer.avatarMask) && !hand;
                if (conflicts)
                {
                    conflictCount++;
                }

                // Cells join the row directly, not through Row's children: .ab-cell carries the gap.
                var row = BridgeElements.Row();
                // Banding, not borders: at fifty-plus layers the eye needs help staying on a line.
                row.EnableInClassList("ab-row-alt", i % 2 == 1);
                row.tooltip = conflicts
                    ? $"Layer {i} sits above the hand-pose layer ({handTop}) and its mask lets it write " +
                      "finger muscles. On Override at weight 1 it replaces whatever pose a gesture just " +
                      "played: the fingers stop moving in game even though the gesture is playing here."
                    : $"Layer {i} \"{layer.name}\": {layer.blendingMode}, default weight " +
                      $"{layer.defaultWeight:0.##}, mask " +
                      (layer.avatarMask != null ? layer.avatarMask.name : "none") + ".";

                var index = Cell(i.ToString(), width: 22);
                BridgeElements.SetTone(index, Tone.Muted);
                row.Add(index);

                // One colour, one meaning: info marks a hand layer, bad a conflict.
                var name = Cell(conflicts ? "⚠ " + layer.name : layer.name, share: 1f);
                BridgeElements.SetTone(name, conflicts ? Tone.Bad : hand ? Tone.Info : Tone.None);
                row.Add(name);

                var weight = Cell("–", width: 44);
                row.Add(weight);

                var mask = Cell(ShortMaskName(layer.avatarMask), width: 88);
                BridgeElements.SetTone(mask, Tone.Muted);
                row.Add(mask);

                var playing = Cell("", share: 1.3f);
                row.Add(playing);

                list.Add(row);
                rows.Add((weight, playing));
            }

            card.SetSummary(conflictCount > 0
                ? $"live · {rows.Count} layers · {conflictCount} may overwrite gestures"
                : $"live · {rows.Count} layers");
            // Collapsed by default, so the header has to show the conflict.
            card.Accent(conflictCount > 0 ? Tone.Bad : Tone.None);

            if (handTop < 0)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No LeftHand/RightHand layer: this avatar's own gesture layers took over the " +
                    "hand pose, so nothing here is checked against them."));
            }

            // 10 Hz: fast enough to read a gesture landing, slow enough to be free. The
            // scheduler stops with the element, so a closed window costs nothing.
            void Tick()
            {
                if (!Application.isPlaying || animator == null)
                {
                    return;
                }
                for (int i = 0; i < rows.Count && i < animator.layerCount; i++)
                {
                    float w = animator.GetLayerWeight(i);
                    rows[i].weight.text = w.ToString("0.00");
                    rows[i].weight.EnableInClassList("ab-text-strong", w > 0.001f);
                    rows[i].weight.EnableInClassList("ab-text-muted", w <= 0.001f);

                    var clips = animator.GetCurrentAnimatorClipInfo(i);
                    var text = new List<string>();
                    foreach (var info in clips)
                    {
                        if (info.clip != null && info.weight > 0.001f)
                        {
                            text.Add($"{info.weight:0.00} {info.clip.name}");
                        }
                    }
                    if (animator.IsInTransition(i))
                    {
                        foreach (var info in animator.GetNextAnimatorClipInfo(i))
                        {
                            if (info.clip != null && info.weight > 0.001f)
                            {
                                text.Add($"→ {info.weight:0.00} {info.clip.name}");
                            }
                        }
                    }
                    rows[i].playing.text = text.Count == 0
                        ? "–"
                        : string.Join(", ", text.GetRange(0, Mathf.Min(3, text.Count)))
                          + (text.Count > 3 ? $" +{text.Count - 3}" : "");
                    rows[i].playing.EnableInClassList("ab-text-muted", text.Count == 0);
                }
            }
            // Once now, or a fresh card shows placeholder dashes until the first tick.
            Tick();
            card.schedule.Execute(Tick).Every(100);

            return card;
        }

        // The merger numbers a second promoted hand layer "LeftHand 2".
        static readonly System.Text.RegularExpressions.Regex HandLayerName =
            new System.Text.RegularExpressions.Regex(@"^(Left|Right)Hand( \d+)?$");
        static bool IsHandLayer(string name) => name != null && HandLayerName.IsMatch(name);

        static bool PermitsFingers(AvatarMask mask)
        {
            return mask != null
                   && (mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers)
                       || mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers));
        }

        // ------------------------------------------------------------ face tracking ----

        static readonly Dictionary<string, float> FaceRest = new Dictionary<string, float>
        {
            { "Direct", 1f }, { "EyeTracking", 1f }, { "FaceTracking", 1f },
            { "EyeTrackingActive", 1f }, { "LipTrackingActive", 1f },
            { "LeftEyeLidExpandedSqueeze", 0.8f }, { "RightEyeLidExpandedSqueeze", 0.8f },
            { "EyeLidLeft", 0.75f }, { "EyeLidRight", 0.75f },
            { "EyesDilation", 0.5f }
        };

        static float FaceRestValue(string param)
        {
            return FaceRest.TryGetValue(AvatarFeatureDetect.FaceTrackingShortName(param), out float v)
                ? v : 0f;
        }

        static string FaceGroup(string name)
        {
            string n = AvatarFeatureDetect.FaceTrackingShortName(name);
            if (n.StartsWith("Left")) { n = n.Substring(4); }
            else if (n.StartsWith("Right")) { n = n.Substring(5); }
            if (n.StartsWith("Tongue")) { return "Tongue"; }
            if (n.StartsWith("Brow")) { return "Brows"; }
            if (n.StartsWith("Cheek") || n.StartsWith("Nose")) { return "Cheeks & nose"; }
            if (n.StartsWith("Lip") || n.StartsWith("MouthClosed")) { return "Lips"; }
            if (n.StartsWith("Jaw")) { return "Jaw"; }
            if (n.StartsWith("Mouth") || n.StartsWith("SmileFrown")) { return "Mouth"; }
            if (n.StartsWith("Eye")) { return "Eyes"; }
            return "Other";
        }

        static readonly string[] FaceGroupOrder =
        {
            "Eyes", "Brows", "Jaw", "Mouth", "Lips", "Cheeks & nose", "Tongue", "Other"
        };

        static Dictionary<string, Vector2> ScanParameterRanges(
            UnityEditor.Animations.AnimatorController asset)
        {
            var ranges = new Dictionary<string, Vector2>();
            if (asset == null)
            {
                return ranges;
            }
            var visited = new HashSet<Motion>();

            void Note(string name, float value)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                ranges[name] = ranges.TryGetValue(name, out var span)
                    ? new Vector2(Mathf.Min(span.x, value), Mathf.Max(span.y, value))
                    : new Vector2(value, value);
            }

            void ScanTree(Motion motion)
            {
                // Trees are shared between states constantly (VRCFury reuses one tree across
                // dozens of toggles), so without this the walk re-descends the same subtree
                // over and over.
                if (!(motion is BlendTree tree) || !visited.Add(motion))
                {
                    return;
                }
                bool oneD = tree.blendType == BlendTreeType.Simple1D;
                foreach (var child in tree.children)
                {
                    Note(tree.blendParameter, oneD ? child.threshold : child.position.x);
                    if (!oneD)
                    {
                        Note(tree.blendParameterY, child.position.y);
                    }
                    ScanTree(child.motion);
                }
            }

            void ScanMachine(AnimatorStateMachine machine)
            {
                if (machine == null)
                {
                    return;
                }
                foreach (var child in machine.states)
                {
                    ScanTree(child.state != null ? child.state.motion : null);
                }
                foreach (var sub in machine.stateMachines)
                {
                    ScanMachine(sub.stateMachine);
                }
            }

            foreach (var layer in asset.layers)
            {
                ScanMachine(layer.stateMachine);
            }
            return ranges;
        }

        static void FaceParamRange(Dictionary<string, Vector2> ranges, string name,
            out float lo, out float hi)
        {
            lo = 0f;
            hi = 1f;
            if (ranges != null && ranges.TryGetValue(name, out var span) && span.x <= span.y)
            {
                lo = Mathf.Min(0f, span.x);
                hi = Mathf.Max(1f, span.y);
            }
        }

        VisualElement BuildFaceTrackingCard(CVRAvatar avatar, bool live)
        {
            var declared = ControllerParameterList(avatar);
            var faceParams = declared.Where(AvatarFeatureDetect.IsFaceTrackingParameter).ToList();

            // Native first: the component reads the headset and writes the
            // mesh, with no parameters. It used to show only when the
            // controller declared none, so one stray TongueOut hid a fully
            // mapped native face behind a slider nothing reads.
            var nativeSetup = avatar != null ? avatar.GetComponentInChildren<CVRFaceTracking>(true) : null;
            bool native = nativeSetup != null && nativeSetup.FaceMesh != null && nativeSetup.FaceBlendShapes != null
                          && nativeSetup.FaceBlendShapes.Any(s => !string.IsNullOrEmpty(s) && s != "-none-");
            var mesh = native ? nativeSetup.FaceMesh.sharedMesh : null;
            var shapes = NativeShapes(nativeSetup, mesh);
            // Beside a native face, gates alone drive nothing worth a section.
            bool showParams = native
                ? faceParams.Any(p => !AvatarFeatureDetect.IsFaceTrackingGate(p))
                : faceParams.Count > 0;

            if (!native && !showParams)
            {
                var none = new BridgeElements.Card("Face tracking", expanded: true).Remember("Tester.Face tracking");
                none.SetSummary("none");
                none.Body.Add(BridgeElements.Empty("No face tracking on this avatar",
                    "Convert with a face tracking mode other than \"Keep the avatar's own rig\", or bring one " +
                    "with a Unified Expressions rig."));
                return none;
            }

            int count = shapes.Count + (showParams ? faceParams.Count : 0);
            var card = new BridgeElements.Card("Face tracking", expanded: count <= BridgeElements.SearchFrom)
                .Remember("Tester.Face tracking");
            card.SetSummary(native
                ? $"{shapes.Count} blendshapes (native)" + (showParams ? $" · {faceParams.Count} parameters" : "")
                : $"{faceParams.Count} parameters");

            if (count > BridgeElements.SearchFrom)
            {
                var search = BridgeElements.SearchField(card);
                search.tooltip = "Find a blendshape or parameter by name";
                card.Body.Add(search);
            }

            // Filled below; the buttons sit above what they reset.
            var shapeSliders = new List<(Slider slider, int index)>();
            var paramSliders = new Dictionary<string, Slider>();
            Button clear = shapes.Count == 0 ? null : BridgeElements.Btn("Clear all shapes", () =>
            {
                var renderer = nativeSetup.FaceMesh;
                if (renderer == null)
                {
                    return;
                }
                Undo.RecordObject(renderer, "Face tracking preview reset");
                foreach (var (slider, index) in shapeSliders)
                {
                    slider.SetValueWithoutNotify(0f);
                    renderer.SetBlendShapeWeight(index, 0f);
                }
            }, "Back to 0, where native shapes rest.");
            Button neutral = !showParams ? null : BridgeElements.Btn("Neutral face", () =>
            {
                var a = LiveAnimator();
                if (a == null)
                {
                    return;
                }
                foreach (var pair in paramSliders)
                {
                    float value = FaceRestValue(pair.Key);
                    pair.Value.SetValueWithoutNotify(value);
                    Drive(a, pair.Key, value);
                }
                // The gates and #Direct have no slider of their own. #Direct is the rig's
                // blend-tree master weight; left at 0 the whole face freezes, which reads as
                // "face tracking is broken". Undeclared names are ignored, like the game does.
                foreach (string param in declared.Where(AvatarFeatureDetect.IsFaceTrackingGate))
                {
                    Drive(a, param, FaceRestValue(param));
                }
                Drive(a, "#Direct", 1f);
                Drive(a, "Direct", 1f);
            }, "The rig's own resting values: eyelids at 0.8 and pupils at 0.5, not zero, which would shut the eyes.");
            neutral?.SetEnabled(live);
            if (clear != null || neutral != null)
            {
                var tools = BridgeElements.ToolbarRow(clear, neutral);
                // Kept through a search: they act on every shape, shown or not.
                tools.AddToClassList("ab-keep");
                card.Body.Add(tools);
            }

            // Not Play-gated: these record Undo and work on the mesh in edit mode too.
            if (native)
            {
                card.Section("Blendshapes");
                if (mesh == null)
                {
                    card.Body.Add(BridgeElements.Hint(
                        $"\"{nativeSetup.FaceMesh.name}\" has no mesh, so its blendshapes can't be driven."));
                }
                else if (shapes.Count == 0)
                {
                    card.Body.Add(BridgeElements.Hint(
                        "No slot names a shape on this mesh. Assign them on the CVRFaceTracking component."));
                }
                else
                {
                    card.Body.Add(BridgeElements.Hint(
                        $"ChilloutVR writes these {shapes.Count} shapes on \"{nativeSetup.FaceMesh.name}\" from the " +
                        "headset. One that moves here is mapped."));
                }
                foreach (string shape in shapes)
                {
                    int index = mesh.GetBlendShapeIndex(shape);
                    var renderer = nativeSetup.FaceMesh;
                    var slider = BridgeElements.SliderField(shape,
                        $"Blendshape {index} on \"{renderer.name}\".\n\nIn game ChilloutVR writes this from the " +
                        $"headset, scaled by Blend Shape Strength ({nativeSetup.BlendShapeStrength:0}%).",
                        0f, 100f, renderer.GetBlendShapeWeight(index), v =>
                        {
                            if (renderer != null)
                            {
                                Undo.RecordObject(renderer, "Face tracking preview");
                                renderer.SetBlendShapeWeight(index, v);
                            }
                        });
                    card.Body.Add(slider);
                    shapeSliders.Add((slider, index));
                }
            }

            if (!showParams)
            {
                return card;
            }

            var animator = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            var asset = runtime as UnityEditor.Animations.AnimatorController;
            var ranges = ScanParameterRanges(asset);

            card.Section("Animator parameters");
            card.Body.Add(BridgeElements.Hint(
                "The parameters VRCFaceTracking drives in game. Shapes moving here prove the rig survived."));

            // The gates first: with one of these at 0 the rig is off and every slider below it
            // looks broken. That is a support question waiting to happen, so they lead.
            foreach (string gate in faceParams.Where(AvatarFeatureDetect.IsFaceTrackingGate))
            {
                string captured = gate;
                var toggle = BridgeElements.Bind(AvatarFeatureDetect.FaceTrackingShortName(gate),
                    $"{gate}\n\nThe rig's own master switch for this half. At 0 nothing below moves, however " +
                    "hard it is driven.",
                    FaceRestValue(gate) > 0f, on => Drive(LiveAnimator(), captured, on ? 1f : 0f));
                toggle.SetEnabled(live);
                card.Body.Add(toggle);
            }

            foreach (string group in FaceGroupOrder)
            {
                // The gates already have their own toggles above.
                var inGroup = faceParams
                    .Where(p => !AvatarFeatureDetect.IsFaceTrackingGate(p) && FaceGroup(p) == group)
                    .OrderBy(AvatarFeatureDetect.FaceTrackingShortName).ToList();
                if (inGroup.Count == 0)
                {
                    continue;
                }
                card.Section(group);
                foreach (string param in inGroup)
                {
                    FaceParamRange(ranges, param, out float lo, out float hi);
                    var slider = BridgeElements.SliderField(AvatarFeatureDetect.FaceTrackingShortName(param),
                        $"{param}   ({lo:0.##} to {hi:0.##}, read from the rig's own blend trees)",
                        lo, hi, FaceRestValue(param), v => Drive(LiveAnimator(), param, v));
                    slider.SetEnabled(live);
                    card.Body.Add(slider);
                    paramSliders[param] = slider;
                }
            }
            return card;
        }

        // Distinct, in mapping order, skipping the CCK's empty-slot marker and any shape the
        // mesh does not have.
        static List<string> NativeShapes(CVRFaceTracking native, Mesh mesh)
        {
            var shapes = new List<string>();
            if (native == null || mesh == null || native.FaceBlendShapes == null)
            {
                return shapes;
            }
            var seen = new HashSet<string>();
            foreach (string shape in native.FaceBlendShapes)
            {
                if (!string.IsNullOrEmpty(shape) && shape != "-none-" && seen.Add(shape)
                    && mesh.GetBlendShapeIndex(shape) >= 0)
                {
                    shapes.Add(shape);
                }
            }
            return shapes;
        }

        // Column widths are data: a fixed column gets a width, a flexible one a share of what
        // is left, so every row lines up under the header.
        static Label Cell(string text, float width = 0f, float share = 0f)
        {
            var label = new Label(text);
            label.AddToClassList("ab-cell");
            if (width > 0f)
            {
                label.style.width = width;
                label.style.flexShrink = 0;
            }
            else
            {
                label.style.flexGrow = share;
                label.style.flexShrink = 1;
                label.style.flexBasis = 0;
                label.style.minWidth = 0;
            }
            return label;
        }

        static string ShortMaskName(AvatarMask mask)
        {
            if (mask == null)
            {
                return "none";
            }
            switch (mask.name)
            {
                case "AvatarBridge_NoMuscles": return "no muscles";
                case "AvatarBridge_FingersOnly": return "fingers";
                case "AvatarBridge_HandLeft": return "L hand";
                case "AvatarBridge_HandRight": return "R hand";
                case "AvatarBridge_HandsOnly": return "hands";
                case "AvatarBridge_FullBody": return "full body";
            }
            return mask.name.StartsWith("AvatarBridge_")
                ? mask.name.Substring("AvatarBridge_".Length)
                : mask.name;
        }

        static float? ReadParam(Animator animator, string name)
        {
            if (!TryParameterType(animator, name, out var type))
            {
                return null;
            }
            switch (type)
            {
                case AnimatorControllerParameterType.Float: return animator.GetFloat(name);
                case AnimatorControllerParameterType.Int: return animator.GetInteger(name);
                case AnimatorControllerParameterType.Bool: return animator.GetBool(name) ? 1f : 0f;
                default: return null;
            }
        }

        void BuildMenuControls(BridgeElements.Card card, CVRAvatar avatar, bool live)
        {
            var parent = card.Body;
            var settings = avatar != null && avatar.avatarSettings != null
                ? avatar.avatarSettings.settings
                : null;
            if (settings == null || settings.Count == 0)
            {
                parent.Add(BridgeElements.Hint("No Advanced Avatar Settings entries on this avatar."));
                return;
            }
            var quiet = live ? avatar.GetComponentInChildren<Animator>(true) : null;

            // The card follows the controller actually on the Animator.
            // Undeclared entries grey out with the reason; the
            // fingerprint poll rebuilds on any controller change.
            var declared = new HashSet<string>(ControllerParameterList(avatar));
            var watched = avatar.GetComponentInChildren<Animator>(true);
            if (watched == null || watched.runtimeAnimatorController == null)
            {
                parent.Add(BridgeElements.Hint("No animator controller assigned: every entry stays greyed until one is."));
            }

            // Menus routinely run past thirty entries; a filter beats scrolling. It reads each
            // entry's tooltip too, so a parameter name finds its entry.
            if (settings.Count > BridgeElements.SearchFrom)
            {
                var search = BridgeElements.SearchField(card);
                search.tooltip = "Find a menu entry by its name or the parameter it drives";
                parent.Add(search);
            }
            int noticeAt = parent.childCount;

            // Every entry hover-reveals the parameter it drives.
            // The menu shows labels; bug reports talk machine names.
            int missingCount = 0;
            void Register(VisualElement element, string parameterName, bool missing)
            {
                if (missing)
                {
                    element.tooltip = $"\"{parameterName}\" is not in the animator controller, so it drives nothing.";
                    missingCount++;
                }
                else
                {
                    element.tooltip = $"drives \"{parameterName}\"";
                }
                element.SetEnabled(live && !missing);
                parent.Add(element);
            }

            foreach (var entry in settings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.machineName))
                {
                    continue;
                }
                string parameter = entry.machineName;
                string label = string.IsNullOrEmpty(entry.name) ? parameter : entry.name;
                switch (entry.type)
                {
                    case CVRAdvancedSettingsEntry.SettingsType.Toggle:
                        var toggle = BridgeElements.Bind(label, null, (ReadParam(quiet, parameter) ?? 0f) != 0f,
                            on => Drive(LiveAnimator(), parameter, on ? 1f : 0f));
                        Register(toggle, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Slider:
                        var slider = BridgeElements.SliderField(label, null, 0f, 1f, ReadParam(quiet, parameter) ?? 0f,
                            v => Drive(LiveAnimator(), parameter, v));
                        slider.AddToClassList("ab-field-wide");
                        Register(slider, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Dropdown:
                        var dropdown = entry.setting as CVRAdvancesAvatarSettingGameObjectDropdown;
                        var names = new List<string>();
                        if (dropdown != null && dropdown.options != null)
                        {
                            foreach (var option in dropdown.options)
                            {
                                string name = option != null && !string.IsNullOrEmpty(option.name)
                                    ? option.name : $"option {names.Count}";
                                // A popup resolves a pick by its label, so a repeated
                                // label would always drive the first option carrying it.
                                string unique = name;
                                for (int n = 2; names.Contains(unique); n++)
                                {
                                    unique = $"{name} ({n})";
                                }
                                names.Add(unique);
                            }
                        }
                        if (names.Count == 0)
                        {
                            names.Add("option 0");
                        }
                        int current = Mathf.Clamp(
                            Mathf.RoundToInt(ReadParam(quiet, parameter) ?? 0f), 0, names.Count - 1);
                        var choice = BridgeElements.Popup(label, null, names.ToArray(), current,
                            index => Drive(LiveAnimator(), parameter, index));
                        choice.AddToClassList("ab-field-wide");
                        Register(choice, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                        foreach (string axis in new[] { "x", "y" })
                        {
                            string driven = parameter + "-" + axis;
                            var stick = BridgeElements.SliderField($"{label} {axis.ToUpperInvariant()}", null, -1f, 1f,
                                ReadParam(quiet, driven) ?? 0f, v => Drive(LiveAnimator(), driven, v));
                            stick.AddToClassList("ab-field-wide");
                            Register(stick, driven, !declared.Contains(driven));
                        }
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.InputSingle:
                        // The kit has no float field; this is the one free-number entry.
                        var input = new FloatField(label) { value = ReadParam(quiet, parameter) ?? 0f };
                        input.AddToClassList("ab-field");
                        input.AddToClassList("ab-field-wide");
                        input.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, e.newValue));
                        Register(input, parameter, !declared.Contains(parameter));
                        break;
                    default:
                        // An explanation, so never dimmed with the controls.
                        var hint = BridgeElements.Hint(
                            $"{label}: {entry.type} isn't driveable from here yet.");
                        hint.tooltip = $"drives \"{parameter}\"";
                        parent.Add(hint);
                        break;
                }
            }
            // Greyed entries: the menu has them, the animator does not yet.
            // Said once, above them, not only in a tooltip.
            if (missingCount > 0)
            {
                var missing = BridgeElements.Notice(Tone.Warn,
                    $"{missingCount} greyed entr{(missingCount == 1 ? "y is" : "ies are")} not in the animator yet. " +
                    "Press Create Animator on the CVRAvatar's Advanced Settings.");
                missing.AddToClassList("ab-keep");
                parent.Insert(noticeAt, missing);
            }
        }
    }
}
#endif
