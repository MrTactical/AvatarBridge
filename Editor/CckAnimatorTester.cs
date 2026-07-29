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
    /// <summary>
    /// A play-mode driver for everything ChilloutVR feeds an avatar — gestures, locomotion,
    /// visemes, emotes and the avatar's own Advanced Avatar Settings menu — so a converted
    /// avatar can be tested in the editor the way it will actually behave in game.
    ///
    /// It exists because of how testers naturally test: VRChat's Gesture Manager on the
    /// ORIGINAL avatar as the "before". That comparison is unwinnable — Gesture Manager needs
    /// the VRC descriptor, which conversion removes. A full tester round was spent "proving"
    /// correct conversions broken that way. This window is the apples-to-apples counterpart:
    /// every control writes the parameters ChilloutVR's client writes (gesture poses via the
    /// GestureLeft/GestureRight floats the CCK's own layers condition on), coerced by the
    /// declared type exactly as the client does.
    /// </summary>
    public class CckAnimatorTester : EditorWindow
    {
        [MenuItem("Tools/Avatar Bridge/CCK Animator Tester")]
        static void Open()
        {
            var window = GetWindow<CckAnimatorTester>();
            window.titleContent = new GUIContent("CCK Animator Tester");
            window.minSize = new Vector2(360, 420);
        }

        static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
            "nn", "RR", "aa", "E", "I", "O", "U"
        };

        static readonly (string name, int value)[] Poses =
        {
            ("Idle", 0), ("Open", -1), ("Fist", 1), ("Thumbs", 2),
            ("Gun", 3), ("Point", 4), ("Peace", 5), ("RnR", 6)
        };

        const string LayersOpenKey = "AvatarBridge.Tester.LayersOpen";

        CVRAvatar _override;
        float _loudness = 1f;
        int _fingerprint;
        double _nextPoll;
        // Read in OnEnable, NEVER as a field initializer. An EditorWindow is a ScriptableObject,
        // and Unity forbids EditorPrefs there:
        //   "GetBool is not allowed to be called from a ScriptableObject constructor (or
        //    instance field initializer), call it in OnEnable instead."
        // It throws during construction and leaves the window half-built — which renders as an
        // unthemed window full of blank cards, looks exactly like a styling bug, and cost four
        // releases of chasing stylesheets before the editor log was read.
        bool _layersOpen;

        void OnEnable()
        {
            _layersOpen = EditorPrefs.GetBool(LayersOpenKey, false);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Selection.selectionChanged += Rebuild;
            // The menu card mirrors whatever controller sits on the avatar's Animator RIGHT
            // NOW. The CCK regenerates that controller when Advanced Avatar Settings change,
            // and a conversion swaps it wholesale — polling a cheap fingerprint keeps the card
            // true without the user having to know a refresh is a thing.
            EditorApplication.update += PollForChanges;
        }

        /// <summary>
        /// The build lives here and not in OnEnable, which is where it lived for this window's
        /// whole life: OnEnable runs before the window's panel attaches, and a tree built that
        /// early renders with the wrong editor theme after some domain reloads. CreateGUI is
        /// the callback Unity provides for exactly this, and it is the one the converter
        /// window has always used — the two windows disagreeing side by side on the same build
        /// is what finally pointed here.
        /// </summary>
        void CreateGUI()
        {
            // Structured exactly like AvatarBridgeWindow.CreateGUI, because that window has
            // never once rendered in the wrong theme and this one would not stop.
            //
            // The skin and the stylesheet are applied HERE and never again. Build() used to
            // re-apply them on every rebuild — including rebuilds fired from the
            // EditorApplication.update poll, where isProSkin is not dependable. A single false
            // reading there swaps the root to .light, and because rebuilds only happen when the
            // avatar's controller changes, nothing comes along to put it back. That is the
            // difference between the two windows: this one asked the question hundreds of times
            // in the wrong place, the converter asks it once in the right one.
            //
            // Rebuild only ever Clears CHILDREN, so the class and the stylesheet set up here
            // survive every rebuild untouched.
            var root = rootVisualElement;
            root.AddToClassList("ab-root");
            BridgeTheme.ApplySkin(root);
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }
            Rebuild();
        }

        /// <summary>Focus is a genuine GUI context, so it is the safe place to notice the user
        /// switched the editor theme. Costs two class toggles.</summary>
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
            Selection.selectionChanged -= Rebuild;
            EditorApplication.update -= PollForChanges;
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Rebuild();

        void PollForChanges()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
            {
                return;
            }
            _nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (ComputeFingerprint() != _fingerprint)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Cheap identity of "what the window is showing": which avatar, which controller its
        /// Animator carries, which parameters that controller declares, and which AAS entries
        /// exist. Any of those changing means the built controls are stale. Rebuilding ONLY on
        /// a changed fingerprint matters as much as rebuilding at all — a rebuild mid-drag
        /// would yank the slider out from under the cursor.
        /// </summary>
        int ComputeFingerprint()
        {
            var avatar = ResolveAvatar();
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

        /// <summary>
        /// The parameters the avatar's CURRENT controller declares, in declaration order.
        /// Read from the controller asset rather than Animator.parameters so it works outside
        /// play mode too; an override controller answers with its base's list, which is the
        /// list the animator actually runs with.
        /// </summary>
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
            // A conversion scene usually holds several CVRAvatars — the greyed-out original,
            // sometimes a thumbnail rig — so "first found" picked the wrong one for a tester.
            // Prefer an active avatar whose animator actually has a controller.
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

        /// <summary>
        /// The animator to drive, or null with a console note SAYING WHICH LINK FAILED —
        /// "not found" with an avatar plainly in the scene is a bug report about the error
        /// message. Resolved at click time, not build time: play mode starts and stops while
        /// the window sits open, and play-mode reloads invalidate cached references.
        /// </summary>
        Animator LiveAnimator()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AvatarBridge] The tester needs play mode — animators only evaluate there.");
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
                Debug.LogWarning($"[AvatarBridge] \"{avatar.name}\"'s Animator has no controller assigned — " +
                                 "was this conversion finished, or the assignment lost with an unsaved scene?");
                return null;
            }
            return animator;
        }

        /// <summary>
        /// Writes a parameter the way the client's SetParameter_Internal does (decompiled):
        /// found by exact name, coerced by the DECLARED type — bool becomes 0/1, ints round.
        /// Undeclared names are ignored silently, exactly like the game.
        /// </summary>
        static void Drive(Animator animator, string name, float value)
        {
            if (animator == null)
            {
                return;
            }
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name != name)
                {
                    continue;
                }
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(name, value);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(name, Mathf.RoundToInt(value));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(name, value != 0f);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (value != 0f)
                        {
                            animator.SetTrigger(name);
                        }
                        break;
                }
                return;
            }
        }

        void Rebuild()
        {
            // Stored up front so the poll doesn't immediately rebuild what was just built.
            _fingerprint = ComputeFingerprint();
            rootVisualElement.Clear();
            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                // A half-built window is indistinguishable from a styling bug — blank cards, no
                // text — and this window has now cost several releases to exactly that confusion.
                // Say what actually happened, in the window, where it cannot be missed.
                Debug.LogException(e);
                var failure = new BridgeElements.Card("The tester failed to build its UI");
                failure.Body.Add(BridgeElements.Hint(
                    $"{e.GetType().Name}: {e.Message}\n\nThe full stack trace is in the Console. " +
                    "This is a bug in AvatarBridge, not in your avatar — please report it with " +
                    "that message."));
                rootVisualElement.Add(failure);
            }
        }

        /// <summary>
        /// Builds one card, and turns a failure into a card that SAYS it failed rather than
        /// taking the rest of the window down with it. Every card here reads whatever the
        /// avatar's controller happens to contain, so one hostile controller should cost its own
        /// card and nothing else.
        /// </summary>
        static VisualElement SafeCard(string what, System.Func<VisualElement> build)
        {
            try
            {
                return build();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                var card = new BridgeElements.Card($"{what} — failed to build");
                card.Body.Add(BridgeElements.Hint($"{e.GetType().Name}: {e.Message}"));
                return card;
            }
        }

        void Build()
        {
            var root = rootVisualElement;
            // Same dress code as the main window: without the stylesheet the cards and banner
            // render as bare labels, which looked exactly as rough as that sounds.
            root.AddToClassList("ab-root");
            // Exclusive, not additive: the root survives every Rebuild, so an added class is
            // permanent — and carrying both skin classes renders light in a dark editor.
            BridgeTheme.ApplySkin(root);
            var sheet = Resources.Load<StyleSheet>("AvatarBridge");
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }

            root.Add(BridgeElements.Banner("CCK Animator Tester",
                "drive a ChilloutVR avatar the way the game does", "v" + BridgeDefines.Version));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 8;
            scroll.style.paddingRight = 8;
            scroll.style.paddingTop = 6;
            root.Add(scroll);

            var avatar = ResolveAvatar();
            bool live = Application.isPlaying && avatar != null;

            // ---- target ------------------------------------------------------------------
            var pick = new BridgeElements.Card("Avatar");
            var field = new ObjectField("CVR avatar")
            {
                objectType = typeof(CVRAvatar),
                allowSceneObjects = true,
                value = _override
            };
            field.RegisterValueChangedCallback(e => { _override = e.newValue as CVRAvatar; Rebuild(); });
            pick.Body.Add(field);
            pick.Body.Add(BridgeElements.Hint(
                avatar == null
                    ? "No ChilloutVR avatar found — select one in the scene, or drop it above."
                    : (Application.isPlaying
                        ? $"Driving \"{avatar.name}\". Every control writes what ChilloutVR itself writes."
                        : $"Found \"{avatar.name}\" — enter PLAY MODE to drive it; animators only evaluate there.")));
            pick.Body.Add(BridgeElements.Hint(
                "VRChat's Gesture Manager cannot drive a converted avatar: it needs the VRC " +
                "descriptor, which conversion removes. This window is the ChilloutVR-side " +
                "equivalent — every control writes exactly what the game writes."));
            scroll.Add(pick);

            // ---- gestures ----------------------------------------------------------------
            var gestures = new BridgeElements.Card("Gestures");
            gestures.Body.Add(PoseRow("Left", "GestureLeft"));
            gestures.Body.Add(PoseRow("Right", "GestureRight"));
            // The game's analog fist: the trigger squeeze IS the gesture — GestureLeft carries
            // the 0..1 grip value and Idx rounds along (decompiled: Gesture = grip in the fist
            // band). Driving only the weight did nothing until the fist state was already
            // active, which read as "slider doesn't work".
            gestures.Body.Add(DrivenSlider("Left trigger (fist curl)", 0f, 1f, 0f, v =>
            {
                var a = LiveAnimator();
                Drive(a, "GestureLeft", v);
                Drive(a, "GestureLeftIdx", Mathf.RoundToInt(v));
                Drive(a, "GestureLeftWeight", v);
            }));
            gestures.Body.Add(DrivenSlider("Right trigger (fist curl)", 0f, 1f, 0f, v =>
            {
                var a = LiveAnimator();
                Drive(a, "GestureRight", v);
                Drive(a, "GestureRightIdx", Mathf.RoundToInt(v));
                Drive(a, "GestureRightWeight", v);
            }));
            gestures.SetEnabled(live);
            scroll.Add(gestures);

            // ---- locomotion --------------------------------------------------------------
            var locomotion = new BridgeElements.Card("Locomotion");
            locomotion.Body.Add(DrivenSlider("Movement X  (strafe)", -1f, 1f, 0f, v =>
            {
                var a = LiveAnimator();
                Drive(a, "MovementX", v);
                Drive(a, "VelocityX", v * 4f); // ~run speed, so velocity-driven trees react too
            }));
            locomotion.Body.Add(DrivenSlider("Movement Y  (forward)", -1f, 1f, 0f, v =>
            {
                var a = LiveAnimator();
                Drive(a, "MovementY", v);
                Drive(a, "VelocityZ", v * 4f);
            }));
            // The state flags are NOT independent — the game computes them together every
            // frame (decompiled BetterBetterCharacterController.Animate):
            //   Grounded = swimming || grounded || sitting || paused — chairs and water KEEP
            //   Grounded true; only jumping, falling and flying clear it. Crouch/prone are
            //   refused while flying, swimming or sitting, and in VR are DERIVED from
            //   Upright (<= 0.4 prone, <= 0.75 crouch — fixed client limits). The CCK layer
            //   agrees: Sitting/Swimming/Crouching/Prone/airborne all branch off Standard
            //   Locomotion one at a time, each held only by its own flag, and Flying is an
            //   AnyState override that interrupts everything, emotes included.
            // So: one exclusive stance writing the exact flag set the game would feed,
            // instead of checkboxes that compose flag soups no client ever produces.
            var quiet = Application.isPlaying && avatar != null
                ? avatar.GetComponentInChildren<Animator>(true)
                : null;
            float Flag(string flagName) => ReadParam(quiet, flagName) ?? 0f;
            Slider upright = null;
            var stanceButtons = new Dictionary<string, Button>();
            // Recover the current stance from the live flags, in the layer's own precedence.
            string stance =
                Flag("Flying") > 0.5f ? "Flying" :
                Flag("Sitting") > 0.5f ? "Sitting" :
                Flag("Swimming") > 0.5f ? "Swimming" :
                Flag("Prone") > 0.5f ? "Prone" :
                Flag("Crouching") > 0.5f ? "Crouching" :
                (ReadParam(quiet, "Grounded") ?? 1f) < 0.5f ? "Airborne" : "Standing";

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
                if (moveUpright && height >= 0f && upright != null)
                {
                    upright.SetValueWithoutNotify(height);
                    Drive(a, "Upright", height);
                }
                stance = name;
                foreach (var pair in stanceButtons)
                {
                    pair.Value.style.unityFontStyleAndWeight =
                        pair.Key == name ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            var stanceRow = new VisualElement();
            stanceRow.style.flexDirection = FlexDirection.Row;
            stanceRow.style.flexWrap = Wrap.Wrap;
            stanceRow.style.alignItems = Align.Center;
            stanceRow.style.marginTop = 4;
            var stanceCaption = new Label("Stance");
            stanceCaption.style.width = 44;
            stanceCaption.style.unityTextAlign = TextAnchor.MiddleLeft;
            stanceRow.Add(stanceCaption);
            foreach (var (name, tip) in new[]
            {
                ("Standing", "Grounded, nothing else — Standard Locomotion."),
                ("Crouching", "Crouching + Grounded, Upright into the crouch band (0.40–0.75). " +
                              "In VR the game derives this from your real height."),
                ("Prone", "Prone + Grounded, Upright below 0.40 — the game's prone threshold."),
                ("Airborne", "Grounded off, nothing else — the jump/fall chain " +
                             "(JumpStart, JumpAir, then JumpLand when Grounded returns)."),
                ("Flying", "Flying on, Grounded off. An AnyState override in the CCK layer — " +
                           "it interrupts every state, emotes included."),
                ("Sitting", "Sitting + Grounded — the game KEEPS Grounded true in chairs."),
                ("Swimming", "Swimming + Grounded — the game keeps Grounded true in water too."),
            })
            {
                string captured = name;
                var stanceButton = new Button(() => DriveStance(captured, moveUpright: true))
                {
                    text = name,
                    tooltip = tip,
                };
                stanceButton.style.marginBottom = 2;
                if (captured == stance)
                {
                    stanceButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                stanceButtons[captured] = stanceButton;
                stanceRow.Add(stanceButton);
            }
            locomotion.Body.Add(stanceRow);

            upright = new Slider("Upright  (1 = standing)", 0f, 1f)
            {
                value = ReadParam(quiet, "Upright") ?? 1f,
                showInputField = true,
                tooltip = "Viewpoint height over avatar height, clamped 0..1. In VR the game " +
                          "derives stance from it — dragging below 0.75 crouches, below 0.40 " +
                          "goes prone, exactly like the client.",
            };
            upright.RegisterValueChangedCallback(e =>
            {
                Drive(LiveAnimator(), "Upright", e.newValue);
                // Mirror the client's VR derivation — but only from a ground stance;
                // CanCrouch/CanProne refuse while flying, swimming or sitting.
                if (stance == "Standing" || stance == "Crouching" || stance == "Prone")
                {
                    string derived = e.newValue <= 0.4f ? "Prone"
                        : e.newValue <= 0.75f ? "Crouching" : "Standing";
                    if (derived != stance)
                    {
                        DriveStance(derived, moveUpright: false);
                    }
                }
            });
            locomotion.Body.Add(upright);

            // AFK is the odd one out: fed from the headset proximity sensor, not the movement
            // system, and nothing in the CCK's locomotion layer reads it — it only reaches
            // avatars that declare an AFK parameter themselves.
            var afk = new Toggle
            {
                text = "AFK",
                value = Flag("AFK") > 0.5f,
                tooltip = "The headset proximity sensor in game. Independent of stance; only " +
                          "does anything if the avatar declares an AFK parameter.",
            };
            afk.style.marginTop = 4;
            afk.RegisterValueChangedCallback(e => Drive(LiveAnimator(), "AFK", e.newValue ? 1f : 0f));
            locomotion.Body.Add(afk);
            locomotion.SetEnabled(live);
            scroll.Add(locomotion);

            // ---- face & emotes -----------------------------------------------------------
            // Visemes and blinking are NOT animator features in ChilloutVR: the client's lip
            // sync and blink controller write BLENDSHAPE WEIGHTS on the face mesh directly.
            // The parameters are still driven for any animator logic that reads them, but the
            // visible mouth comes from the blendshapes — driving only the parameter looked
            // like "visemes don't work" to the first tester who tried.
            var face = new BridgeElements.Card("Face & emotes");
            var viseme = new DropdownField("Viseme", new List<string>(VisemeNames), 0);
            viseme.RegisterValueChangedCallback(e =>
                ApplyViseme(System.Array.IndexOf(VisemeNames, e.newValue), _loudness));
            face.Body.Add(viseme);
            face.Body.Add(DrivenSlider("Viseme loudness", 0f, 1f, 1f, v =>
            {
                _loudness = v;
                ApplyViseme(System.Array.IndexOf(VisemeNames, viseme.value), v);
            }));
            face.Body.Add(DrivenSlider("Blink", 0f, 1f, 0f, ApplyBlink));
            var emoteRow = new VisualElement();
            emoteRow.style.flexDirection = FlexDirection.Row;
            var emoteField = new IntegerField("Emote") { value = 0 };
            emoteField.style.flexGrow = 1;
            emoteRow.Add(emoteField);
            emoteRow.Add(new Button(() => Drive(LiveAnimator(), "Emote", emoteField.value)) { text = "Play" });
            emoteRow.Add(new Button(() =>
            {
                var a = LiveAnimator();
                Drive(a, "Emote", 0f);
                Drive(a, "CancelEmote", 1f);
            }) { text = "Cancel" });
            face.Body.Add(emoteRow);
            face.SetEnabled(live);
            scroll.Add(face);

            // ---- face tracking -----------------------------------------------------------
            var faceTracking = SafeCard("Face tracking", () => BuildFaceTrackingCard(avatar));
            faceTracking.SetEnabled(live);
            scroll.Add(faceTracking);

            // ---- the avatar's own menu ---------------------------------------------------
            var menu = new BridgeElements.Card("Avatar menu  (Advanced Avatar Settings)");
            BuildMenuControls(menu.Body, avatar);
            menu.SetEnabled(live);
            scroll.Add(menu);

            // ---- resting defaults --------------------------------------------------------
            var reset = new BridgeElements.Card("Reset");
            reset.Body.Add(new Button(() =>
            {
                var a = LiveAnimator();
                if (a == null)
                {
                    return;
                }
                // The values the game itself rests at — see the conversion's resting-value pass.
                // Standing writes the whole stance flag set (Grounded 1, everything else 0)
                // and returns Upright to 1, keeping the stance row's highlight honest.
                DriveStance("Standing", moveUpright: true);
                Drive(a, "AFK", 0f);
                afk.SetValueWithoutNotify(false);
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
            }) { text = "Resting defaults  (what the game reports standing still)" });
            reset.SetEnabled(live);
            scroll.Add(reset);

            // ---- what the animator is ACTUALLY doing -------------------------------------
            // Pinned BELOW the scroll view, not inside it. The whole point of a live layer
            // readout is watching it react while a control above is being driven — a toggle
            // flipped in the menu card, a gesture button, a stance change. Inside the scroll
            // the readout left the screen the moment the user scrolled to the control they
            // wanted to drive, which is exactly when it mattered. Down here the cards scroll
            // behind it and the layers stay put; its own rows scroll internally, capped, so
            // fifty layers cannot swallow the window either.
            var layers = SafeCard("Animator layers", () => BuildLayerCard(avatar));
            layers.style.flexShrink = 0;
            layers.style.marginLeft = 8;
            layers.style.marginRight = 8;
            layers.style.marginBottom = 6;
            root.Add(layers);
        }

        /// <summary>
        /// What the client's LipSyncManager does: zero every viseme blendshape on the face
        /// mesh, weight the active one by loudness. Parameters ride along for animator logic.
        /// Only the standard 15-blendshape viseme mode is emulated; jaw-bone and
        /// single-blendshape modes get their parameters and nothing visible.
        /// </summary>
        void ApplyViseme(int index, float loudness)
        {
            var animator = LiveAnimator();
            Drive(animator, "VisemeIdx", index);
            Drive(animator, "VisemeLoudness", loudness);

            var avatar = ResolveAvatar();
            var mesh = avatar != null ? avatar.bodyMesh : null;
            var shared = mesh != null ? mesh.sharedMesh : null;
            if (shared == null || avatar.visemeBlendshapes == null)
            {
                return;
            }
            for (int i = 0; i < avatar.visemeBlendshapes.Length; i++)
            {
                string shapeName = avatar.visemeBlendshapes[i];
                int shape = string.IsNullOrEmpty(shapeName) ? -1 : shared.GetBlendShapeIndex(shapeName);
                if (shape >= 0)
                {
                    mesh.SetBlendShapeWeight(shape, i == index ? loudness * 100f : 0f);
                }
            }
        }

        /// <summary>Blink, the same way — the client writes the blink blendshapes directly.</summary>
        void ApplyBlink(float amount)
        {
            var avatar = ResolveAvatar();
            var mesh = avatar != null ? avatar.bodyMesh : null;
            var shared = mesh != null ? mesh.sharedMesh : null;
            if (shared == null || avatar.blinkBlendshape == null)
            {
                return;
            }
            foreach (var shapeName in avatar.blinkBlendshape)
            {
                int shape = string.IsNullOrEmpty(shapeName) ? -1 : shared.GetBlendShapeIndex(shapeName);
                if (shape >= 0)
                {
                    mesh.SetBlendShapeWeight(shape, amount * 100f);
                }
            }
        }

        VisualElement PoseRow(string label, string floatParameter)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 4;
            row.style.alignItems = Align.Center;
            var caption = new Label(label);
            caption.style.width = 44;
            caption.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(caption);
            foreach (var pose in Poses)
            {
                int value = pose.value;
                row.Add(new Button(() =>
                {
                    var animator = LiveAnimator();
                    // Int drives the discrete pose states, float rides along for surviving
                    // float logic, weight makes the analog fist curl fully — the same trio
                    // the client maintains.
                    Drive(animator, floatParameter + "Idx", value);
                    Drive(animator, floatParameter, value);
                    Drive(animator, floatParameter + "Weight", value == 1 ? 1f : 0f);
                }) { text = pose.name });
            }
            return row;
        }

        /// <summary>
        /// Per layer: its weight, its avatar mask, and the clips it is playing RIGHT NOW —
        /// read from the same Animator API ChilloutVR's own CCK Debugger uses, plus a mask
        /// column the debugger cannot show, because masks live on the controller asset and
        /// only an editor can reach them.
        ///
        /// It exists because of the bug that took five rounds to find. The debugger read
        /// "LeftHand — Layer Weight 1.00, playing Thumbs Up 1.00" while the avatar's fingers
        /// sat in their rest pose, and every check of the animator said it was correct —
        /// because it WAS. Two layers further down the list had masks letting them rewrite the
        /// same muscles afterwards. Either row alone looks fine; the two together are the whole
        /// diagnosis, which is why they had to be on one screen.
        /// </summary>
        VisualElement BuildLayerCard(CVRAvatar avatar)
        {
            // Collapsed by default and remembered — this is a diagnostic, not something to
            // scroll past on every visit, and the fingerprint poll rebuilds the whole window.
            var card = new BridgeElements.Card("Animator layers  (live)",
                summary: null, expanded: _layersOpen,
                onToggle: open => { _layersOpen = open; EditorPrefs.SetBool(LayersOpenKey, open); });
            var animator = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            var asset = runtime as UnityEditor.Animations.AnimatorController;

            if (!Application.isPlaying || animator == null || asset == null)
            {
                card.Body.Add(BridgeElements.Hint(
                    animator == null || asset == null
                        ? "No animator controller to read yet."
                        : "Enter PLAY MODE — layer weights and playing clips only exist while the " +
                          "animator evaluates. This is the same readout ChilloutVR's CCK Debugger " +
                          "shows in game, so what you see here is what a tester would report."));
                return card;
            }

            // Which layers own the hand pose, and therefore which layers ABOVE them are able to
            // ruin it. Highest index wins: it is the one that writes last.
            int handTop = -1;
            for (int i = 0; i < asset.layers.Length; i++)
            {
                if (asset.layers[i].name == "LeftHand" || asset.layers[i].name == "RightHand")
                {
                    handTop = i;
                }
            }

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.Add(Cell("#", 22, TextAnchor.MiddleRight));
            header.Add(Flex("LAYER", 1f));
            header.Add(Cell("WT", 38, TextAnchor.MiddleRight));
            header.Add(Cell("MASK", 76, TextAnchor.MiddleLeft));
            header.Add(Flex("PLAYING", 1.3f));
            foreach (var child in header.Children())
            {
                child.AddToClassList("ab-sub");
                child.style.marginRight = 6;
            }
            card.Body.Add(header);

            // The rows scroll under the fixed column header, capped so the pinned card leaves
            // the controls above it room to breathe. Scrollbar space is why the header sits
            // outside: with it inside, the header would shift left whenever the bar appears.
            var list = new ScrollView();
            list.style.maxHeight = 240;
            card.Body.Add(list);

            var rows = new List<(Label weight, Label playing, VisualElement row)>();
            int conflictCount = 0;
            for (int i = 0; i < animator.layerCount && i < asset.layers.Length; i++)
            {
                var layer = asset.layers[i];
                bool conflicts = i > handTop && handTop >= 0 && PermitsFingers(layer.avatarMask)
                                 && layer.name != "LeftHand" && layer.name != "RightHand";
                if (conflicts)
                {
                    conflictCount++;
                }

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop = 1;
                row.style.paddingBottom = 1;
                // Banding, not borders: at fifty-plus layers the eye needs help staying on a
                // line, and a 4% wash reads in either editor skin.
                if (i % 2 == 1)
                {
                    row.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
                }
                row.tooltip = conflicts
                    ? $"Layer {i} sits ABOVE the hand-pose layer ({handTop}) and its mask lets it write " +
                      "finger muscles. On Override at weight 1 it replaces whatever pose a gesture just " +
                      "played — the fingers stop moving in game even though the gesture is playing here."
                    : $"Layer {i} \"{layer.name}\" — {layer.blendingMode}, default weight " +
                      $"{layer.defaultWeight:0.##}, mask " +
                      (layer.avatarMask != null ? layer.avatarMask.name : "none") + ".";

                var index = Cell(i.ToString(), 22, TextAnchor.MiddleRight);
                index.style.color = BridgeTheme.Muted;
                row.Add(index);

                var name = Flex(conflicts ? "⚠ " + layer.name : layer.name, 1f);
                if (conflicts)
                {
                    name.style.color = BridgeTheme.Bad;
                }
                else if (layer.name == "LeftHand" || layer.name == "RightHand")
                {
                    name.style.color = BridgeTheme.Good;
                }
                row.Add(name);

                var weight = Cell("–", 38, TextAnchor.MiddleRight);
                row.Add(weight);

                var mask = Cell(ShortMaskName(layer.avatarMask), 76, TextAnchor.MiddleLeft);
                mask.style.color = BridgeTheme.Muted;
                row.Add(mask);

                var playing = Flex("", 1.3f);
                row.Add(playing);

                foreach (var child in row.Children())
                {
                    child.style.marginRight = 6;
                }
                list.Add(row);
                rows.Add((weight, playing, row));
            }

            card.SetSummary(conflictCount > 0
                ? $"{rows.Count} layers · {conflictCount} may overwrite gestures"
                : $"{rows.Count} layers");

            if (handTop < 0)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No LeftHand/RightHand layer — this avatar's own gesture layers took over the " +
                    "hand pose, so nothing here is checked against them."));
            }

            // 10 Hz: fast enough to read a gesture landing, slow enough to be free. The
            // scheduler stops with the element, so a closed window costs nothing.
            card.schedule.Execute(() =>
            {
                if (!Application.isPlaying || animator == null)
                {
                    return;
                }
                for (int i = 0; i < rows.Count && i < animator.layerCount; i++)
                {
                    float w = animator.GetLayerWeight(i);
                    rows[i].weight.text = w.ToString("0.00");
                    rows[i].weight.style.color = w > 0.001f ? BridgeTheme.Good : BridgeTheme.Muted;

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
                        ? "—"
                        : string.Join(", ", text.GetRange(0, Mathf.Min(3, text.Count)))
                          + (text.Count > 3 ? $" +{text.Count - 3}" : "");
                    // Null hands the colour back to the stylesheet rather than pinning a
                    // literal one, so the row still reads correctly in both editor skins.
                    rows[i].playing.style.color = text.Count == 0
                        ? new StyleColor(BridgeTheme.Muted)
                        : new StyleColor(StyleKeyword.Null);
                }
            }).Every(100);

            return card;
        }

        static bool PermitsFingers(AvatarMask mask)
        {
            return mask != null
                   && (mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers)
                       || mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers));
        }

        // ------------------------------------------------------------ face tracking ----

        /// <summary>
        /// The eye/face parameters that carry no "v2/" prefix. Everything else is recognised by
        /// that prefix, so an avatar carrying its own Unified Expressions rig is driven here
        /// just as well as the bundled one.
        /// </summary>
        static readonly HashSet<string> PlainFaceParams = new HashSet<string>
        {
            "EyeTracking", "FaceTracking", "EyesY", "LeftEyeX", "RightEyeX",
            "LeftEyeLidExpandedSqueeze", "RightEyeLidExpandedSqueeze", "EyesDilation"
        };

        /// <summary>Resting values that are NOT zero — a face tracking rig parked at 0
        /// everywhere has its eyes shut and its pupils pinned. From the rig's own readme.</summary>
        static readonly Dictionary<string, float> FaceRest = new Dictionary<string, float>
        {
            { "#Direct", 1f }, { "EyeTracking", 1f }, { "FaceTracking", 1f },
            { "LeftEyeLidExpandedSqueeze", 0.8f }, { "RightEyeLidExpandedSqueeze", 0.8f },
            { "EyesDilation", 0.5f }
        };

        static bool IsFaceParam(string name)
        {
            return name.StartsWith("v2/") || PlainFaceParams.Contains(name);
        }

        /// <summary>
        /// Which section a parameter belongs under. Classified from the name rather than a fixed
        /// table, so a rig that adds shapes still files them somewhere sensible. Side and version
        /// markers come off first: "v2/MouthUpperUpLeft" and "LeftEyeX" both have to reduce to
        /// something the prefix tests can read.
        /// </summary>
        static string FaceGroup(string name)
        {
            string n = name.StartsWith("v2/") ? name.Substring(3) : name;
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

        /// <summary>
        /// A parameter's real range, read from the blend trees that consume it rather than
        /// guessed from its name. Unified Expressions mixes 0..1 shapes with -1..1 bipolar ones
        /// (JawX, SmileFrown, CheekPuffSuck, the tongue axes), and a slider with the wrong
        /// bounds either cannot reach half the expression or invents travel the rig ignores.
        /// The rig itself is the only authority on which is which, so it is asked.
        /// </summary>
        /// <summary>
        /// Every blend parameter's range, from ONE walk of the controller.
        ///
        /// The first version of this asked the controller per parameter, which is the same walk
        /// fifty times over — and on a 58-layer avatar carrying VRCFury's deep Direct trees that
        /// is not a slow build, it is a window that never finishes one, re-entered twice a
        /// second by the change poll. It rendered as blank cards, which looks exactly like the
        /// styling bug this window had the week before, and cost another release to tell apart.
        /// Walk once, answer from a dictionary.
        /// </summary>
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

        /// <summary>
        /// A parameter's slider bounds, never narrower than 0..1 — a parameter used only as a
        /// gate has a single threshold and would otherwise collapse to a zero-width slider.
        /// </summary>
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

        /// <summary>
        /// Drives the eye and face tracking parameters the way ChilloutVR's VRCFaceTracking
        /// bridge drives them — the same Drive path as every other control here, coerced by the
        /// declared type.
        ///
        /// It reads the parameters off the avatar's own controller, so it covers the bundled
        /// CVR-VRCFT rig and any avatar carrying its own Unified Expressions setup equally, and
        /// shows nothing at all when the avatar has no face tracking. The editor cannot prove a
        /// headset will feed these — only wearing it can — but it does prove the SHAPES move,
        /// which is the half that breaks silently in conversion.
        /// </summary>
        VisualElement BuildFaceTrackingCard(CVRAvatar avatar)
        {
            var card = new BridgeElements.Card("Face tracking");
            var declared = ControllerParameterList(avatar);
            var faceParams = declared.Where(IsFaceParam).ToList();

            if (faceParams.Count == 0)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No face tracking parameters on this avatar's controller. Convert with " +
                    "\"CVR-VRCFT\" face tracking selected, or bring an avatar that carries its " +
                    "own Unified Expressions rig."));
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

            card.SetSummary($"{faceParams.Count} parameters");
            card.Body.Add(BridgeElements.Hint(
                "Drives the same parameters the VRCFaceTracking bridge drives in game. Blendshapes " +
                "moving here is what proves the rig survived conversion; whether a headset feeds " +
                "them is only answerable in game."));

            var sliders = new Dictionary<string, Slider>();

            // The two gates first: with these at 0 the rig is off and every slider below looks
            // broken. That is a support question waiting to happen, so they lead.
            foreach (string gate in new[] { "EyeTracking", "FaceTracking" })
            {
                if (!declared.Contains(gate))
                {
                    continue;
                }
                var toggle = new Toggle(gate) { value = true };
                toggle.tooltip = $"The rig's own master switch for this half. At 0 nothing below " +
                                 "moves, however hard it is driven.";
                toggle.RegisterValueChangedCallback(e => Drive(LiveAnimator(), gate, e.newValue ? 1f : 0f));
                card.Body.Add(toggle);
            }

            var searchable = new List<(VisualElement element, string key)>();
            if (faceParams.Count > 12)
            {
                var search = new ToolbarSearchField();
                search.style.width = Length.Percent(100);
                search.style.marginTop = 4;
                search.style.marginBottom = 4;
                search.RegisterValueChangedCallback(e =>
                {
                    string query = (e.newValue ?? "").ToLowerInvariant();
                    foreach (var (element, key) in searchable)
                    {
                        element.style.display = key.Contains(query) ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                });
                card.Body.Add(search);
            }

            foreach (string group in FaceGroupOrder)
            {
                // The two gates already have their own toggles above.
                var inGroup = faceParams
                    .Where(p => p != "EyeTracking" && p != "FaceTracking" && FaceGroup(p) == group)
                    .OrderBy(p => p).ToList();
                if (inGroup.Count == 0)
                {
                    continue;
                }
                var heading = BridgeElements.SubHeading(group);
                heading.style.marginTop = 6;
                card.Body.Add(heading);
                searchable.Add((heading, group.ToLowerInvariant()));

                foreach (string param in inGroup)
                {
                    FaceParamRange(ranges, param, out float lo, out float hi);
                    float start = FaceRest.TryGetValue(param, out float rest) ? rest : 0f;
                    string label = param.StartsWith("v2/") ? param.Substring(3) : param;
                    var slider = new Slider(label, lo, hi)
                    {
                        value = start,
                        showInputField = true,
                        tooltip = $"{param}   ({lo:0.##} to {hi:0.##}, read from the rig's own blend trees)",
                    };
                    slider.RegisterValueChangedCallback(e => Drive(LiveAnimator(), param, e.newValue));
                    card.Body.Add(slider);
                    sliders[param] = slider;
                    searchable.Add((slider, label.ToLowerInvariant()));
                }
            }

            var restButton = new Button(() =>
            {
                var a = LiveAnimator();
                if (a == null)
                {
                    return;
                }
                foreach (var pair in sliders)
                {
                    float value = FaceRest.TryGetValue(pair.Key, out float rest) ? rest : 0f;
                    pair.Value.SetValueWithoutNotify(value);
                    Drive(a, pair.Key, value);
                }
                // #Direct is the rig's blend-tree master weight and has no slider of its own;
                // left at 0 the whole face freezes, which reads as "face tracking is broken".
                foreach (var pair in FaceRest)
                {
                    Drive(a, pair.Key, pair.Value);
                }
            })
            {
                text = "Neutral face  (the rig's own resting values)",
                tooltip = "Not all zero: eyelids rest at 0.8 and pupils at 0.5. Zeroing everything " +
                          "shuts the eyes and pins the pupils, which looks like a broken rig.",
            };
            restButton.style.marginTop = 6;
            card.Body.Add(restButton);
            return card;
        }

        /// <summary>Fixed-width cell. Never shrinks, so the columns stay in line.</summary>
        static Label Cell(string text, float width, TextAnchor align)
        {
            var label = Clipped(text);
            label.style.width = width;
            label.style.flexShrink = 0;
            label.style.flexGrow = 0;
            label.style.unityTextAlign = align;
            return label;
        }

        /// <summary>
        /// Proportional cell. <c>flexBasis 0</c> and <c>minWidth 0</c> are the whole trick:
        /// without them a flex item refuses to shrink below its text width, so one long layer
        /// name shoves every column after it off its grid — which is exactly how the first
        /// version of this card looked.
        /// </summary>
        static Label Flex(string text, float grow)
        {
            var label = Clipped(text);
            label.style.flexGrow = grow;
            label.style.flexShrink = 1;
            label.style.flexBasis = 0;
            label.style.minWidth = 0;
            return label;
        }

        static Label Clipped(string text)
        {
            var label = new Label(text);
            label.style.overflow = Overflow.Hidden;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.fontSize = 11;
            return label;
        }

        /// <summary>
        /// Masks are named for the file system, not for reading fifty at a time. "AvatarBridge_"
        /// on every row is pure noise; what matters is what the mask lets through.
        /// </summary>
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

        static VisualElement DrivenSlider(string label, float lo, float hi, float initial,
            System.Action<float> onChange)
        {
            var slider = new Slider(label, lo, hi) { value = initial, showInputField = true };
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            return slider;
        }

        /// <summary>Current value of a declared parameter, or null — so controls can open
        /// showing the avatar's ACTUAL state in play mode instead of factory defaults.</summary>
        static float? ReadParam(Animator animator, string name)
        {
            if (animator == null)
            {
                return null;
            }
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name != name)
                {
                    continue;
                }
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: return animator.GetFloat(name);
                    case AnimatorControllerParameterType.Int: return animator.GetInteger(name);
                    case AnimatorControllerParameterType.Bool: return animator.GetBool(name) ? 1f : 0f;
                    default: return null;
                }
            }
            return null;
        }

        void BuildMenuControls(VisualElement parent, CVRAvatar avatar)
        {
            var settings = avatar != null && avatar.avatarSettings != null
                ? avatar.avatarSettings.settings
                : null;
            if (settings == null || settings.Count == 0)
            {
                parent.Add(BridgeElements.Hint("No Advanced Avatar Settings entries on this avatar."));
                return;
            }
            var live = Application.isPlaying && avatar != null
                ? avatar.GetComponentInChildren<Animator>(true)
                : null;

            // The card follows the controller actually on the Animator: entries whose
            // parameter it doesn't declare are greyed with the reason — driving them would do
            // nothing, in game or here — and the fingerprint poll rebuilds this card the
            // moment the controller (or its parameter list) changes.
            var declared = new HashSet<string>(ControllerParameterList(avatar));
            var watched = avatar != null ? avatar.GetComponentInChildren<Animator>(true) : null;
            var watchedController = watched != null ? watched.runtimeAnimatorController : null;
            parent.Add(BridgeElements.Hint(watchedController != null
                ? $"Reading \"{watchedController.name}\" — this card refreshes itself when the " +
                  "controller or its parameters change."
                : "No animator controller assigned — every entry stays greyed until one is."));

            // Menus routinely run past thirty entries; a filter beats scrolling. Rows register
            // themselves with their searchable text and the filter just flips display.
            var rows = new List<(VisualElement element, string key)>();
            if (settings.Count > 8)
            {
                var search = new ToolbarSearchField();
                search.style.width = Length.Percent(100);
                search.style.marginBottom = 5;
                search.RegisterValueChangedCallback(e =>
                {
                    string query = e.newValue ?? "";
                    foreach (var (element, key) in rows)
                    {
                        element.style.display =
                            key.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0
                                ? DisplayStyle.Flex
                                : DisplayStyle.None;
                    }
                });
                parent.Add(search);
            }
            // Every entry hover-reveals the parameter it drives — the menu shows the avatar
            // author's labels, but bug reports talk in machine names.
            int missingCount = 0;
            void Register(VisualElement element, string entryLabel, string parameterName,
                bool missing = false)
            {
                if (missing)
                {
                    element.SetEnabled(false);
                    element.tooltip = $"\"{parameterName}\" is not declared in the current " +
                        "animator controller — driving it would do nothing, in game or here. " +
                        "It lights up the moment the controller declares it.";
                    missingCount++;
                }
                else
                {
                    element.tooltip = $"drives \"{parameterName}\"";
                }
                rows.Add((element, entryLabel + "\n" + parameterName));
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
                        var toggle = new Toggle(label) { value = (ReadParam(live, parameter) ?? 0f) != 0f };
                        // The main window's checkbox-first row: boxes align in a column and the
                        // whole row highlights under the cursor, instead of each checkbox
                        // trailing its own label at a different x.
                        toggle.AddToClassList("ab-toggle");
                        toggle.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, e.newValue ? 1f : 0f));
                        Register(toggle, label, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Slider:
                        var slider = DrivenSlider(label, 0f, 1f, ReadParam(live, parameter) ?? 0f,
                            v => Drive(LiveAnimator(), parameter, v));
                        slider.AddToClassList("ab-field");
                        slider.AddToClassList("ab-field-wide");
                        Register(slider, label, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Dropdown:
                        var dropdown = entry.setting as CVRAdvancesAvatarSettingGameObjectDropdown;
                        var names = new List<string>();
                        if (dropdown != null && dropdown.options != null)
                        {
                            foreach (var option in dropdown.options)
                            {
                                names.Add(option != null && !string.IsNullOrEmpty(option.name)
                                    ? option.name : $"option {names.Count}");
                            }
                        }
                        if (names.Count == 0)
                        {
                            names.Add("option 0");
                        }
                        int current = Mathf.Clamp(
                            Mathf.RoundToInt(ReadParam(live, parameter) ?? 0f), 0, names.Count - 1);
                        var choice = new DropdownField(label, names, current);
                        choice.AddToClassList("ab-field");
                        choice.AddToClassList("ab-field-wide");
                        choice.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, names.IndexOf(e.newValue)));
                        Register(choice, label, parameter, !declared.Contains(parameter));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                        var joyX = DrivenSlider($"{label} X", -1f, 1f, ReadParam(live, parameter + "-x") ?? 0f,
                            v => Drive(LiveAnimator(), parameter + "-x", v));
                        joyX.AddToClassList("ab-field");
                        joyX.AddToClassList("ab-field-wide");
                        Register(joyX, label, parameter + "-x", !declared.Contains(parameter + "-x"));
                        var joyY = DrivenSlider($"{label} Y", -1f, 1f, ReadParam(live, parameter + "-y") ?? 0f,
                            v => Drive(LiveAnimator(), parameter + "-y", v));
                        joyY.AddToClassList("ab-field");
                        joyY.AddToClassList("ab-field-wide");
                        Register(joyY, label, parameter + "-y", !declared.Contains(parameter + "-y"));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.InputSingle:
                        var input = new FloatField(label) { value = ReadParam(live, parameter) ?? 0f };
                        input.AddToClassList("ab-field");
                        input.AddToClassList("ab-field-wide");
                        input.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, e.newValue));
                        Register(input, label, parameter, !declared.Contains(parameter));
                        break;
                    default:
                        var hint = BridgeElements.Hint(
                            $"{label}: {entry.type} isn't driveable from here yet.");
                        Register(hint, label, parameter);
                        break;
                }
            }
        }
    }
}
#endif
