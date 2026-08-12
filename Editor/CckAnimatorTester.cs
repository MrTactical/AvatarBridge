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
        // Read in OnEnable, never as a field initializer. Unity forbids
        // EditorPrefs in a ScriptableObject constructor; the throw
        // leaves the window half-built and unthemed.
        bool _layersOpen;

        void OnEnable()
        {
            _layersOpen = EditorPrefs.GetBool(LayersOpenKey, false);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Selection.selectionChanged += Rebuild;
            // The menu card mirrors the Animator's current controller.
            // The CCK regenerates it and conversions swap it wholesale;
            // polling a cheap fingerprint keeps the card true.
            EditorApplication.update += PollForChanges;
            // onBeforeRender and not EditorApplication.update: it fires inside the player loop
            // after LateUpdate and before anything is drawn, which is exactly where the client
            // writes these weights. An editor-update write lands at an unspecified point around
            // the frame and loses to the animator often enough to look broken.
            Application.onBeforeRender += HoldFaceShapes;
        }

        void CreateGUI()
        {
            // Skin and stylesheet applied here and never again.
            // isProSkin is not dependable inside update polls, and one
            // false reading would stick. Rebuild only clears children,
            // so this setup survives every rebuild.
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
            Application.onBeforeRender -= HoldFaceShapes;
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Rebuild();

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
            CacheFaceShapes();
            if (ComputeFingerprint() != _fingerprint)
            {
                Rebuild();
            }
        }

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
            CacheFaceShapes();
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
                    "This is a bug in AvatarBridge, not in your avatar — please report it with " +
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
                var card = new BridgeElements.Card($"{what} — failed to build");
                card.Body.Add(BridgeElements.Hint($"{e.GetType().Name}: {e.Message}"));
                return card;
            }
        }

        void Build()
        {
            var root = rootVisualElement;
            // Without the stylesheet the cards render as bare labels.
            root.AddToClassList("ab-root");
            // Exclusive, not additive. The root survives every Rebuild,
            // and carrying both skin classes renders light in dark.
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
            // The game's analog fist: the trigger squeeze is the
            // gesture. GestureLeft carries the 0..1 grip value in the
            // fist band; driving only the weight does nothing.
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
            // The state flags are not independent; the client computes
            // them together. Chairs and water keep Grounded true, crouch
            // and prone derive from Upright in VR, Flying interrupts
            // everything. So: one exclusive stance writing the exact
            // flag set the game would feed, never a flag soup.
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
                // Mirror the client's VR derivation, ground stances only.
                // CanCrouch/CanProne refuse while flying, swimming, sitting.
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

            // AFK is fed from the headset proximity sensor. Only
            // reaches avatars that declare an AFK parameter.
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
            // Visemes and blinking are not animator features in CVR;
            // the client writes blendshape weights on the mesh
            // directly. Parameters are still driven for animator logic,
            // but the visible mouth comes from the blendshapes.
            var face = new BridgeElements.Card("Face & emotes");
            // Held on the mesh every frame; backing fields must start
            // where the controls start.
            _viseme = 0;
            _loudness = 1f;
            _blink = 0f;
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
            face.Body.Add(BridgeElements.Hint(
                "Visemes and blink are held on the face mesh every frame, after the animator — the " +
                "same place and order ChilloutVR writes them. So they beat any animation using the " +
                "same blendshape, here and in game. An expression that stops closing the eyes while " +
                "this window is open would do the same thing in game."));
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
                // The values the game itself rests at. Standing writes
                // the whole stance flag set and returns Upright to 1.
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

            // ---- the avatar as other players run it ----
            // Remote copies never receive "#" locals, and streams are
            // stripped from them, so those parameters sit at their
            // defaults forever. An animator depending on a live local
            // value can behave differently for others: a layer that looks parked to
            // the wearer can cycle between states for everyone else, which in game reads as an
            // animation rapidly looping that the wearer cannot see at all.
            var remote = new BridgeElements.Card("Remote view");
            remote.Body.Add(BridgeElements.Hint(
                "Snaps every \"#\" local parameter to its default — the value it holds forever " +
                "on OTHER players' clients, which never receive local parameters or parameter " +
                "streams. Watch the Animator layers readout after pressing: any layer that " +
                "starts cycling or lands in a different state here is doing exactly that in " +
                "game for everyone but you. Synced parameters are untouched; drive them above " +
                "to reproduce what others see during a toggle."));
            remote.Body.Add(new Button(() =>
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
            }) { text = "Snap \"#\" locals to their defaults  (how remote clients hold them)" });
            remote.SetEnabled(live);
            scroll.Add(remote);

            // ---- live layer readout ----
            // Pinned below the scroll view, not inside it. The point is
            // watching layers react while a control is driven, so the
            // readout must stay on screen. Its own rows scroll internally, so
            // fifty layers cannot swallow the window either.
            var layers = SafeCard("Animator layers", () => BuildLayerCard(avatar));
            layers.style.flexShrink = 0;
            layers.style.marginLeft = 8;
            layers.style.marginRight = 8;
            layers.style.marginBottom = 6;
            root.Add(layers);
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

        void CacheFaceShapes()
        {
            var avatar = ResolveAvatar();
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
                    // Int drives the pose states, float rides along,
                    // weight curls the analog fist. The client's trio.
                    Drive(animator, floatParameter + "Idx", value);
                    Drive(animator, floatParameter, value);
                    Drive(animator, floatParameter + "Weight", value == 1 ? 1f : 0f);
                }) { text = pose.name });
            }
            return row;
        }

        VisualElement BuildLayerCard(CVRAvatar avatar)
        {
            // Collapsed by default and remembered. A diagnostic, not
            // something to scroll past on every visit.
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

        VisualElement BuildFaceTrackingCard(CVRAvatar avatar)
        {
            var card = new BridgeElements.Card("Face tracking");
            var declared = ControllerParameterList(avatar);
            var faceParams = declared.Where(AvatarFeatureDetect.IsFaceTrackingParameter).ToList();

            // Native is checked first. A native avatar declares no
            // per-expression parameters at all; the component reads the
            // headset and writes the mesh. Zero parameters is what a
            // correct native conversion looks like.
            if (!faceParams.Any(p => !AvatarFeatureDetect.IsFaceTrackingGate(p)))
            {
                var nativeSetup = avatar != null ? avatar.GetComponentInChildren<CVRFaceTracking>(true) : null;
                if (nativeSetup != null && nativeSetup.FaceMesh != null && nativeSetup.FaceBlendShapes != null
                    && nativeSetup.FaceBlendShapes.Any(s => !string.IsNullOrEmpty(s) && s != "-none-"))
                {
                    BuildNativeFaceShapeSliders(card, nativeSetup);
                    return card;
                }
            }

            if (faceParams.Count == 0)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No face tracking on this avatar: the controller declares no face-tracking " +
                    "parameters and there is no CVRFaceTracking component with shapes mapped. " +
                    "Convert with \"Native CVR Component\" or \"CVR-VRCFT\" selected, or bring an " +
                    "avatar that carries its own Unified Expressions rig."));
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

            // The gates first: with one of these at 0 the rig is off and every slider below it
            // looks broken. That is a support question waiting to happen, so they lead.
            foreach (string gate in faceParams.Where(AvatarFeatureDetect.IsFaceTrackingGate))
            {
                string captured = gate;
                var toggle = new Toggle(AvatarFeatureDetect.FaceTrackingShortName(gate))
                {
                    value = FaceRestValue(gate) > 0f,
                    tooltip = $"{gate}\n\nThe rig's own master switch for this half. At 0 nothing " +
                              "below moves, however hard it is driven.",
                };
                toggle.RegisterValueChangedCallback(e =>
                    Drive(LiveAnimator(), captured, e.newValue ? 1f : 0f));
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
                // The gates already have their own toggles above.
                var inGroup = faceParams
                    .Where(p => !AvatarFeatureDetect.IsFaceTrackingGate(p) && FaceGroup(p) == group)
                    .OrderBy(AvatarFeatureDetect.FaceTrackingShortName).ToList();
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
                    string label = AvatarFeatureDetect.FaceTrackingShortName(param);
                    var slider = new Slider(label, lo, hi)
                    {
                        value = FaceRestValue(param),
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

        void BuildNativeFaceShapeSliders(BridgeElements.Card card, CVRFaceTracking native)
        {
            var mesh = native.FaceMesh.sharedMesh;
            if (mesh == null)
            {
                card.Body.Add(BridgeElements.Hint(
                    $"\"{native.FaceMesh.name}\" has no mesh, so its blendshapes can't be driven."));
                return;
            }

            // Distinct, in mapping order, skipping the CCK's empty-slot marker.
            var shapes = new List<string>();
            var seen = new HashSet<string>();
            foreach (string shape in native.FaceBlendShapes)
            {
                if (!string.IsNullOrEmpty(shape) && shape != "-none-" && seen.Add(shape)
                    && mesh.GetBlendShapeIndex(shape) >= 0)
                {
                    shapes.Add(shape);
                }
            }

            card.SetSummary($"{shapes.Count} blendshapes  (native)");
            card.Body.Add(BridgeElements.Hint(
                $"This avatar uses ChilloutVR's NATIVE face tracking, so there are no per-expression " +
                $"animator parameters to drive — CVRFaceTracking writes these {shapes.Count} blendshapes on " +
                $"\"{native.FaceMesh.name}\" straight from the headset. The sliders below make the same " +
                "writes, so a shape that moves is mapped to real geometry. Whether a headset feeds it is " +
                "only answerable in game."));

            if (shapes.Count == 0)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No slot names a blendshape that exists on this mesh — the mapping is empty. " +
                    "Assign shapes on the CVRFaceTracking component, or convert again with a face " +
                    "mesh whose shapes follow Unified Expressions naming."));
                return;
            }

            var searchable = new List<(VisualElement element, string key)>();
            if (shapes.Count > 12)
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

            var built = new List<(Slider slider, int index)>();
            foreach (string shape in shapes)
            {
                int index = mesh.GetBlendShapeIndex(shape);
                var renderer = native.FaceMesh;
                var slider = new Slider(shape, 0f, 100f)
                {
                    value = renderer.GetBlendShapeWeight(index),
                    showInputField = true,
                    tooltip = $"Blendshape {index} on \"{renderer.name}\".\n\nIn game ChilloutVR writes " +
                              $"this from the headset, scaled by Blend Shape Strength ({native.BlendShapeStrength:0}%).",
                };
                slider.RegisterValueChangedCallback(e =>
                {
                    if (renderer != null)
                    {
                        Undo.RecordObject(renderer, "Face tracking preview");
                        renderer.SetBlendShapeWeight(index, e.newValue);
                    }
                });
                card.Body.Add(slider);
                built.Add((slider, index));
                searchable.Add((slider, shape.ToLowerInvariant()));
            }

            var reset = new Button(() =>
            {
                var renderer = native.FaceMesh;
                if (renderer == null)
                {
                    return;
                }
                Undo.RecordObject(renderer, "Face tracking preview reset");
                foreach (var (slider, index) in built)
                {
                    slider.SetValueWithoutNotify(0f);
                    renderer.SetBlendShapeWeight(index, 0f);
                }
            })
            {
                text = "Clear all shapes  (back to 0)",
                tooltip = "Native shapes rest at 0 — unlike the VRCFT rig, whose eyelids and pupils " +
                          "rest part-open. These are driven from the headset, so 0 is the true rest.",
            };
            reset.style.marginTop = 6;
            card.Body.Add(reset);
        }

        static Label Cell(string text, float width, TextAnchor align)
        {
            var label = Clipped(text);
            label.style.width = width;
            label.style.flexShrink = 0;
            label.style.flexGrow = 0;
            label.style.unityTextAlign = align;
            return label;
        }

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

            // The card follows the controller actually on the Animator.
            // Undeclared entries grey out with the reason; the
            // fingerprint poll rebuilds on any controller change.
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
            // Every entry hover-reveals the parameter it drives.
            // The menu shows labels; bug reports talk machine names.
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
