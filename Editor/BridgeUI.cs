using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// The window's visual kit — banner, tabs, cards, chips, primary button.
    ///
    /// Kept apart from the window itself because the window's job is deciding *what* to show and
    /// this is only about how it looks. Everything is plain IMGUI drawing into rects; there are no
    /// bundled textures beyond a single generated gradient, so nothing here can go missing on
    /// import.
    ///
    /// Both editor skins are handled. Colours are expressed as alpha over the current background
    /// rather than fixed greys, so a card is the same idea in either theme instead of a light
    /// patch that only works in one.
    /// </summary>
    internal static class BridgeUI
    {
        static bool Dark => EditorGUIUtility.isProSkin;

        // ChilloutVR's own panel is orange; this is deliberately not, so two docked windows never
        // look like the same tool. Blue also reads as "in progress" next to the CCK's "publish".
        public static Color Accent => Dark ? new Color(0.22f, 0.53f, 0.84f) : new Color(0.15f, 0.42f, 0.74f);
        static Color AccentDeep => Dark ? new Color(0.13f, 0.28f, 0.50f) : new Color(0.09f, 0.24f, 0.46f);

        public static Color Good => Dark ? new Color(0.48f, 0.79f, 0.48f) : new Color(0.20f, 0.55f, 0.24f);
        public static Color Warn => Dark ? new Color(0.90f, 0.70f, 0.35f) : new Color(0.72f, 0.50f, 0.10f);
        public static Color Bad => Dark ? new Color(0.90f, 0.45f, 0.45f) : new Color(0.72f, 0.20f, 0.20f);
        public static Color Muted => Dark ? new Color(1f, 1f, 1f, 0.45f) : new Color(0f, 0f, 0f, 0.50f);

        static Color CardFill => Dark ? new Color(1f, 1f, 1f, 0.032f) : new Color(0f, 0f, 0f, 0.028f);
        static Color HeaderFill => Dark ? new Color(1f, 1f, 1f, 0.045f) : new Color(0f, 0f, 0f, 0.04f);
        static Color DividerCol => Dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.10f);

        // ---------------------------------------------------------------- styles ----

        static GUIStyle _bannerTitle, _bannerSub, _bannerVersion, _cardTitle, _cardSummary,
                        _subHeading, _rich, _stepNum, _stepTitle, _chip, _tab, _tabOn;
        static bool _builtDark;

        static void Build()
        {
            if (_bannerTitle != null && _builtDark == Dark)
            {
                return;
            }
            _builtDark = Dark;

            _bannerTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17, alignment = TextAnchor.LowerLeft,
                normal = { textColor = Color.white }, richText = true,
            };
            _bannerSub = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10, alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.72f) },
            };
            _bannerVersion = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.92f) },
            };
            _cardTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _cardSummary = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight, normal = { textColor = Muted },
            };
            _subHeading = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Muted } };
            _rich = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 11 };
            _stepNum = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            _stepTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _chip = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 10,
            };
            _tab = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 11,
                normal = { textColor = Muted },
            };
            _tabOn = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 11,
            };
        }

        public static GUIStyle Rich { get { Build(); return _rich; } }

        // -------------------------------------------------------------- gradient ----

        static Texture2D _grad;
        static bool _gradDark;

        static Texture2D Gradient()
        {
            if (_grad != null && _gradDark == Dark)
            {
                return _grad;
            }
            _gradDark = Dark;
            _grad = new Texture2D(64, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int x = 0; x < 64; x++)
            {
                _grad.SetPixel(x, 0, Color.Lerp(AccentDeep, Accent, x / 63f));
            }
            _grad.Apply();
            return _grad;
        }

        // ----------------------------------------------------------------- pieces ----

        /// <summary>Title bar. Full-bleed, so it reads as chrome rather than as content.</summary>
        public static void Banner(string title, string subtitle, string version)
        {
            Build();
            var rect = GUILayoutUtility.GetRect(0, 52, GUILayout.ExpandWidth(true));
            // Cancel the window's own margin so the bar touches all three edges.
            rect.xMin -= 4;
            rect.xMax += 4;
            rect.yMin -= 4;
            GUI.DrawTexture(rect, Gradient(), ScaleMode.StretchToFill);

            var text = new Rect(rect.x + 14, rect.y + 8, rect.width - 100, 22);
            GUI.Label(text, title, _bannerTitle);
            GUI.Label(new Rect(text.x, text.yMax + 1, text.width, 14), subtitle, _bannerSub);

            if (!string.IsNullOrEmpty(version))
            {
                var size = _bannerVersion.CalcSize(new GUIContent(version));
                var pill = new Rect(rect.xMax - size.x - 26, rect.y + 15, size.x + 14, 18);
                EditorGUI.DrawRect(pill, new Color(0f, 0f, 0f, 0.22f));
                GUI.Label(pill, version, _bannerVersion);
            }
            GUILayout.Space(8);
        }

        /// <summary>Tab strip with an accent underline on the active tab.</summary>
        public static int Tabs(int current, GUIContent[] labels)
        {
            Build();
            var row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            float w = row.width / labels.Length;
            EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), DividerCol);

            for (int i = 0; i < labels.Length; i++)
            {
                var cell = new Rect(row.x + i * w, row.y, w, row.height);
                bool on = i == current;
                if (on)
                {
                    EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 2, cell.width, 2), Accent);
                }
                else if (cell.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(cell, HeaderFill);
                }
                GUI.Label(cell, labels[i], on ? _tabOn : _tab);
                if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                {
                    current = i;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }
            GUILayout.Space(6);
            return current;
        }

        /// <summary>A numbered step marker: filled circle-ish badge plus a title.</summary>
        public static void Step(int number, string title)
        {
            Build();
            GUILayout.Space(6);
            var row = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            var badge = new Rect(row.x, row.y + 2, 17, 17);
            EditorGUI.DrawRect(badge, Accent);
            GUI.Label(badge, number.ToString(), _stepNum);
            GUI.Label(new Rect(badge.xMax + 8, row.y, row.width - 25, row.height), title, _stepTitle);
            GUILayout.Space(4);
        }

        // ------------------------------------------------------------------ cards ----

        /// <summary>
        /// Card + collapsible header in one call, so a section can't leave the layout stack
        /// unbalanced by returning early: callers wrap the body in `if (expanded)` instead.
        /// </summary>
        public static bool CardStart(string title, bool expanded, string summary = null, bool collapsible = true)
        {
            Build();
            EditorGUILayout.BeginVertical(GUIStyle.none);
            var header = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, HeaderFill);
            EditorGUI.DrawRect(new Rect(header.x, header.y, 2, header.height), Accent);

            var label = new Rect(header.x + 10, header.y, header.width - 20, header.height);
            if (collapsible)
            {
                // Drawn rather than made a real Foldout control: the whole header row is the hit
                // target below, and a live Foldout here would swallow the click first.
                var arrow = new Rect(header.x + 8, header.y + 5, 13, 13);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorStyles.foldout.Draw(arrow, false, false, expanded, false);
                }
                label.x += 14;
                label.width -= 14;
            }
            GUI.Label(label, title, _cardTitle);

            if (!string.IsNullOrEmpty(summary))
            {
                GUI.Label(new Rect(header.x, header.y, header.width - 10, header.height), summary, _cardSummary);
            }

            if (collapsible && Event.current.type == EventType.MouseDown
                && header.Contains(Event.current.mousePosition))
            {
                expanded = !expanded;
                GUI.changed = true;
                Event.current.Use();
            }
            return expanded;
        }

        static GUIStyle _bodyPad;

        /// <summary>Body region of a card. Always paired with <see cref="BodyEnd"/>.</summary>
        public static void BodyStart()
        {
            _bodyPad ??= new GUIStyle { padding = new RectOffset(10, 10, 8, 8) };
            var rect = EditorGUILayout.BeginVertical(_bodyPad);
            EditorGUI.DrawRect(rect, CardFill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), new Color(Accent.r, Accent.g, Accent.b, 0.35f));
        }

        public static void BodyEnd()
        {
            EditorGUILayout.EndVertical();
        }

        public static void CardEnd()
        {
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        /// <summary>Small grey heading for a run of related toggles inside a card.</summary>
        public static void SubHeading(string text)
        {
            Build();
            GUILayout.Space(6);
            EditorGUILayout.LabelField(text.ToUpperInvariant(), _subHeading);
        }

        public static void Divider()
        {
            GUILayout.Space(6);
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), DividerCol);
            GUILayout.Space(6);
        }

        static GUIStyle _hint;

        /// <summary>Hint line under a control, in the muted colour.</summary>
        public static void Hint(string text)
        {
            Build();
            _hint ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            _hint.normal.textColor = Muted;
            EditorGUILayout.LabelField(text, _hint);
        }

        // ----------------------------------------------------------------- widgets ----

        static GUIStyle _primary, _chipDraw;

        /// <summary>The one button the window exists for. Accent-filled, tall, unmissable.</summary>
        public static bool PrimaryButton(string label, bool enabled)
        {
            Build();
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);
            var fill = !enabled ? new Color(Accent.r, Accent.g, Accent.b, 0.28f)
                     : hover ? Color.Lerp(Accent, Color.white, 0.12f)
                     : Accent;
            EditorGUI.DrawRect(rect, fill);

            _primary ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 13,
            };
            _primary.normal.textColor = new Color(1f, 1f, 1f, enabled ? 1f : 0.55f);
            GUI.Label(rect, label, _primary);

            if (enabled && Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            if (hover)
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            return false;
        }

        /// <summary>A count + label chip, used for the report's tallies.</summary>
        public static void Chip(string text, Color colour, bool emphasise = false)
        {
            Build();
            var content = new GUIContent(text);
            float w = _chip.CalcSize(content).x + 16;
            var rect = GUILayoutUtility.GetRect(w, 19, GUILayout.Width(w));
            EditorGUI.DrawRect(rect, new Color(colour.r, colour.g, colour.b, emphasise ? 0.22f : 0.12f));
            _chipDraw ??= new GUIStyle(_chip);
            _chipDraw.normal.textColor = emphasise ? colour : Muted;
            GUI.Label(rect, content, _chipDraw);
        }

        static GUIStyle _betaTag;

        /// <summary>An "experimental" marker, so those toggles read as opt-in rather than broken.</summary>
        public static void BetaTag()
        {
            Build();
            var rect = GUILayoutUtility.GetRect(52, 15, GUILayout.Width(52));
            rect.y += 2;
            EditorGUI.DrawRect(rect, new Color(Warn.r, Warn.g, Warn.b, 0.18f));
            _betaTag ??= new GUIStyle(_chip);
            _betaTag.normal.textColor = Warn;
            GUI.Label(rect, "BETA", _betaTag);
        }
    }
}
