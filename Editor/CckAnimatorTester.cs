#if CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
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
    /// the VRC descriptor (removed by conversion) and drives the float GestureLeft, while the
    /// converted gesture logic reads the integer GestureLeftIdx that only the game writes. A
    /// full tester round was spent "proving" correct conversions broken that way. This window
    /// is the apples-to-apples counterpart: every control writes the parameters ChilloutVR's
    /// client writes, coerced by the declared type exactly as the client does.
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

        CVRAvatar _override;

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Selection.selectionChanged += Rebuild;
            Rebuild();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Selection.selectionChanged -= Rebuild;
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Rebuild();

        CVRAvatar ResolveAvatar()
        {
            if (_override != null)
            {
                return _override;
            }
            var selected = Selection.activeGameObject;
            var fromSelection = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            return fromSelection != null ? fromSelection : FindObjectOfType<CVRAvatar>();
        }

        /// <summary>
        /// The animator to drive, or null with a console note when driving is impossible.
        /// Resolved at click time, not build time — play mode starts and stops while the
        /// window sits open.
        /// </summary>
        Animator LiveAnimator()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AvatarBridge] The tester needs play mode — animators only evaluate there.");
                return null;
            }
            var avatar = ResolveAvatar();
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[AvatarBridge] No ChilloutVR avatar with an animator found — select one or set it in the tester window.");
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
            rootVisualElement.Clear();
            Build();
        }

        void Build()
        {
            var root = rootVisualElement;
            root.Add(BridgeElements.Banner("CCK Animator Tester",
                "drive a ChilloutVR avatar the way the game does", BridgeDefines.Version));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
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
                "VRChat's Gesture Manager cannot drive a converted avatar: it needs the removed " +
                "VRC descriptor, and converted gesture logic reads the integer parameters only " +
                "the game feeds. This window is the ChilloutVR-side equivalent."));
            scroll.Add(pick);

            // ---- gestures ----------------------------------------------------------------
            var gestures = new BridgeElements.Card("Gestures");
            gestures.Body.Add(PoseRow("Left", "GestureLeft"));
            gestures.Body.Add(PoseRow("Right", "GestureRight"));
            gestures.Body.Add(DrivenSlider("Left trigger (fist curl)", 0f, 1f, 0f,
                v => { var a = LiveAnimator(); Drive(a, "GestureLeftWeight", v); }));
            gestures.Body.Add(DrivenSlider("Right trigger (fist curl)", 0f, 1f, 0f,
                v => { var a = LiveAnimator(); Drive(a, "GestureRightWeight", v); }));
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
            locomotion.Body.Add(DrivenSlider("Upright  (1 = standing)", 0f, 1f, 1f,
                v => Drive(LiveAnimator(), "Upright", v)));
            var states = new VisualElement();
            states.style.flexDirection = FlexDirection.Row;
            states.style.flexWrap = Wrap.Wrap;
            foreach (var stateName in new[] { "Grounded", "Crouching", "Prone", "Flying", "Swimming", "Sitting", "AFK" })
            {
                string parameter = stateName;
                var toggle = new Toggle(stateName) { value = parameter == "Grounded" };
                toggle.style.marginRight = 8;
                toggle.RegisterValueChangedCallback(e => Drive(LiveAnimator(), parameter, e.newValue ? 1f : 0f));
                states.Add(toggle);
            }
            locomotion.Body.Add(states);
            locomotion.SetEnabled(live);
            scroll.Add(locomotion);

            // ---- face & emotes -----------------------------------------------------------
            var face = new BridgeElements.Card("Face & emotes");
            var viseme = new DropdownField("Viseme", new List<string>(VisemeNames), 0);
            viseme.RegisterValueChangedCallback(e =>
                Drive(LiveAnimator(), "VisemeIdx", System.Array.IndexOf(VisemeNames, e.newValue)));
            face.Body.Add(viseme);
            face.Body.Add(DrivenSlider("Viseme loudness", 0f, 1f, 0f,
                v => Drive(LiveAnimator(), "VisemeLoudness", v)));
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
                Drive(a, "IsLocal", 1f);
                Drive(a, "Grounded", 1f);
                Drive(a, "Upright", 1f);
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
        }

        VisualElement PoseRow(string label, string floatParameter)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            var caption = new Label(label);
            caption.style.width = 40;
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

        static VisualElement DrivenSlider(string label, float lo, float hi, float initial,
            System.Action<float> onChange)
        {
            var slider = new Slider(label, lo, hi) { value = initial, showInputField = true };
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            return slider;
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
                        var toggle = new Toggle(label);
                        toggle.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, e.newValue ? 1f : 0f));
                        parent.Add(toggle);
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Slider:
                        parent.Add(DrivenSlider(label, 0f, 1f, 0f,
                            v => Drive(LiveAnimator(), parameter, v)));
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
                        var choice = new DropdownField(label, names, 0);
                        choice.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, names.IndexOf(e.newValue)));
                        parent.Add(choice);
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                        parent.Add(DrivenSlider($"{label}  X", -1f, 1f, 0f,
                            v => Drive(LiveAnimator(), parameter + "-x", v)));
                        parent.Add(DrivenSlider($"{label}  Y", -1f, 1f, 0f,
                            v => Drive(LiveAnimator(), parameter + "-y", v)));
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.InputSingle:
                        var input = new FloatField(label);
                        input.RegisterValueChangedCallback(e =>
                            Drive(LiveAnimator(), parameter, e.newValue));
                        parent.Add(input);
                        break;
                    default:
                        parent.Add(BridgeElements.Hint(
                            $"{label}: {entry.type} isn't driveable from here yet."));
                        break;
                }
            }
        }
    }
}
#endif
