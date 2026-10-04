using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // Highest first below the primary: a card's own action, an in-place action, a destructive one.
    internal enum ButtonKind { Secondary, Strong, Danger }

    // The window's building blocks.
    //
    // Elements are constructed in C# rather than declared in UXML on purpose: UXML would put the
    // layout in one file and the wiring in another, joined by string names looked up with Q<>,
    // and a typo in that join fails silently at runtime. Built here, construction and wiring are
    // the same line of code, and the compiler checks it. The stylesheet still carries everything
    // USS is actually better at; the cascade, :hover, and the two skins.
    //
    // Every colour, size and gap is a class in AvatarBridge.uss. Inline style here is limited to
    // brand-ramp colours computed per element (banner, active tab, step badge, primary button).
    internal static class BridgeElements
    {
        // ---------------------------------------------------------------- root ----

        static StyleSheet _sheet;

        // CreateGUI or CreateInspectorGUI only: OnEnable runs before the panel attaches and
        // rendered the light skin in a dark editor after some domain reloads.
        public static VisualElement Root(VisualElement root, bool inspector = false)
        {
            root.AddToClassList("ab-root");
            if (inspector)
            {
                root.AddToClassList("ab-inspector");
            }
            BridgeTheme.ApplySkin(root);
            if (_sheet == null)
            {
                _sheet = Resources.Load<StyleSheet>("AvatarBridge");
            }
            if (_sheet != null && !root.styleSheets.Contains(_sheet))
            {
                root.styleSheets.Add(_sheet);
            }
            return root;
        }

        public static ScrollView Scroll()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            scroll.AddToClassList("ab-scroll");
            return scroll;
        }

        public static VisualElement Footer(params Button[] links) => Container("ab-footer", links);

        // Rebuilding from inside a control's own callback destroys it mid-dispatch. One frame later
        // the event has finished with it.
        public static void Defer(VisualElement anyAttached, Action action)
        {
            anyAttached.schedule.Execute(action);
        }

        // -------------------------------------------------------------- banner ----

        public static VisualElement Banner(string title, string subtitle, string pill, BridgeTheme.Span span)
        {
            var bar = new VisualElement();
            bar.AddToClassList("ab-banner");
            bar.style.backgroundImage = new StyleBackground(BridgeTheme.Gradient(span));

            var text = new VisualElement();
            text.AddToClassList("ab-banner-text");

            var name = new Label(title);
            name.AddToClassList("ab-banner-title");
            var sub = new Label(subtitle);
            sub.AddToClassList("ab-banner-sub");
            text.Add(name);
            text.Add(sub);
            bar.Add(text);

            if (!string.IsNullOrEmpty(pill))
            {
                var tag = new Label(pill);
                tag.AddToClassList("ab-version");
                bar.Add(tag);
            }
            return bar;
        }

        // ---------------------------------------------------------------- tabs ----

        // onSelect runs a frame after the click, since every caller rebuilds the strip it sits in.
        public static VisualElement Tabs(string[] labels, string[] icons, int current,
            Action<int> onSelect, BridgeTheme.Span span)
        {
            var strip = new VisualElement();
            strip.AddToClassList("ab-tabs");

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var tab = new Button(() => Defer(strip, () => onSelect(index))) { tooltip = labels[i] };
                tab.AddToClassList("ab-tab");
                if (i == current)
                {
                    tab.AddToClassList("ab-tab-on");
                    // The active tab is tinted to its own place on the window's span, so switching
                    // modes moves you along it rather than just highlighting a button.
                    tab.style.backgroundColor = span.At(labels.Length == 1 ? 0f : i / (float)(labels.Length - 1));
                }

                var icon = BridgeTheme.GetIcon(icons != null && index < icons.Length ? icons[index] : null, tinted: true) as Texture2D;
                if (icon != null)
                {
                    var image = new VisualElement { pickingMode = PickingMode.Ignore };
                    image.AddToClassList("ab-tab-icon");
                    image.style.backgroundImage = new StyleBackground(icon);
                    tab.Add(image);
                }
                tab.Add(new Label(labels[i]) { pickingMode = PickingMode.Ignore });
                strip.Add(tab);
            }
            return strip;
        }

        // ---------------------------------------------------------------- card ----

        // A card. expanded null means it doesn't collapse.
        public class Card : VisualElement
        {
            static readonly string[] AccentClasses =
            {
                "ab-card-accent-info", "ab-card-accent-good", "ab-card-accent-warn",
                "ab-card-accent-bad", "ab-card-accent-muted",
            };

            public readonly VisualElement Body = new VisualElement();
            readonly VisualElement _header;
            readonly Label _title;
            readonly Label _summary;
            readonly Label _arrow;
            VisualElement _badge;
            string _rememberKey;
            bool _open = true;

            public Card(string title, string summary = null, bool? expanded = null)
            {
                AddToClassList("ab-card");
                Body.AddToClassList("ab-card-body");

                _header = new VisualElement();
                _header.AddToClassList("ab-card-header");

                if (expanded.HasValue)
                {
                    AddToClassList("ab-card-collapsible");
                    _open = expanded.Value;
                    _arrow = new Label { pickingMode = PickingMode.Ignore };
                    _arrow.AddToClassList("ab-card-arrow");
                    _header.Add(_arrow);

                    // Release to confirm, like a button, and reachable from the keyboard.
                    _header.focusable = true;
                    _header.AddManipulator(new Clickable(Flip));
                    _header.RegisterCallback<KeyDownEvent>(e =>
                    {
                        if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Space)
                        {
                            return;
                        }
                        Flip();
                        e.StopPropagation();
                    });
                }

                _title = new Label(title) { pickingMode = PickingMode.Ignore };
                _title.AddToClassList("ab-card-title");
                _header.Add(_title);

                _summary = new Label { pickingMode = PickingMode.Ignore };
                _summary.AddToClassList("ab-card-summary");
                _header.Add(_summary);
                SetSummary(summary);

                Add(_header);
                Add(Body);
                Apply();
            }

            public string Title => _title.text;

            // Programmatic: Remember writes back only what the user toggles.
            public bool Open
            {
                get => _open;
                set
                {
                    _open = value;
                    Apply();
                }
            }

            // Badge coloured from its place along the window's span.
            public Card Step(int step, int of, BridgeTheme.Span span)
            {
                if (_badge == null)
                {
                    _badge = new VisualElement { pickingMode = PickingMode.Ignore };
                    _badge.AddToClassList("ab-step-badge");
                    _badge.Add(new Label { pickingMode = PickingMode.Ignore });
                    // Arrow, badge, title, summary.
                    _header.Insert(_arrow != null ? 1 : 0, _badge);
                }
                _badge.style.backgroundColor = span.At(of <= 1 ? 0f : (step - 1) / (float)(of - 1));
                _badge.Q<Label>().text = step.ToString();
                return this;
            }

            // A section fold inside another card's body.
            public Card Nested()
            {
                AddToClassList("ab-fold");
                return this;
            }

            // A 3px stripe down the left in the tone's fill. Tone.None clears it.
            public Card Accent(Tone tone)
            {
                foreach (var c in AccentClasses)
                {
                    RemoveFromClassList(c);
                }
                string suffix = Suffix(tone);
                if (suffix != null)
                {
                    AddToClassList("ab-card-accent-" + suffix);
                }
                return this;
            }

            // Restores Open from EditorPrefs and writes it back on every user toggle.
            public Card Remember(string key)
            {
                _rememberKey = "AvatarBridge.Card." + key;
                if (_arrow != null)
                {
                    Open = EditorPrefs.GetBool(_rememberKey, _open);
                }
                return this;
            }

            // A heading in Body. The first one carries no rule above it, since USS has no :first-child.
            public Label Section(string title)
            {
                var heading = SubHeading(title, Body.childCount == 0);
                Body.Add(heading);
                return heading;
            }

            // A caption in Body. The first one drops its top margin, as Section does.
            public Label Caption(string text)
            {
                var caption = BridgeElements.Caption(text);
                caption.EnableInClassList("ab-caption-first", Body.childCount == 0);
                Body.Add(caption);
                return caption;
            }

            public void SetSummary(string text)
            {
                _summary.text = text ?? string.Empty;
                _summary.EnableInClassList("ab-hidden", string.IsNullOrEmpty(text));
            }

            void Flip()
            {
                _open = !_open;
                Apply();
                if (_rememberKey != null)
                {
                    EditorPrefs.SetBool(_rememberKey, _open);
                }
            }

            void Apply()
            {
                Body.EnableInClassList("ab-hidden", !_open);
                if (_arrow != null)
                {
                    _arrow.text = _open ? "▼" : "▶";
                }
            }
        }

        // ------------------------------------------------------------- headings ----

        // For containers that are not a card body; a card uses Card.Section, which works out first.
        public static Label SubHeading(string text, bool first = false)
        {
            var label = new Label(text);
            label.AddToClassList("ab-section");
            if (first)
            {
                label.AddToClassList("ab-section-first");
            }
            return label;
        }

        // A small bold label over a control group, such as a segmented row.
        public static Label Caption(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ab-caption");
            return label;
        }

        public static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ab-hint");
            return label;
        }

        // Indents a note, or a chip row, to the label of the toggle above it, so it reads as
        // that toggle's.
        public static T UnderToggle<T>(T element) where T : VisualElement
        {
            element.AddToClassList("ab-under-toggle");
            return element;
        }

        // A short line with the rest behind More. Search still finds the hidden part, since
        // Filter reads every Label.
        public static VisualElement Hint(string text, string more)
        {
            var block = new VisualElement();
            block.AddToClassList("ab-hint-block");
            block.Add(Hint(text));
            if (!string.IsNullOrEmpty(more))
            {
                // More sits at the end of the short line; the rest takes a line of its own.
                var rest = Hint(more);
                rest.AddToClassList("ab-hint-rest");
                AddMore(block, rest);
            }
            return block;
        }

        static void AddMore(VisualElement into, Label rest)
        {
            rest.AddToClassList("ab-hidden");
            into.Add(rest);
            Button link = null;
            link = TextLink("More", () =>
            {
                bool show = rest.ClassListContains("ab-hidden");
                rest.EnableInClassList("ab-hidden", !show);
                link.text = show ? "Less" : "More";
            });
            link.AddToClassList("ab-more");
            into.Add(link);
        }

        // -------------------------------------------------------------- key-value ----

        public sealed class KeyValueRow : VisualElement
        {
            public readonly Label Value;

            internal KeyValueRow(string key, string value, Tone tone, string tooltip)
            {
                AddToClassList("ab-kv");
                this.tooltip = tooltip;
                var k = new Label(key);
                k.AddToClassList("ab-kv-key");
                Add(k);
                Value = new Label();
                Value.AddToClassList("ab-kv-value");
                Add(Value);
                Set(value, tone);
            }

            public void Set(string text, Tone tone = Tone.None)
            {
                Value.text = text ?? string.Empty;
                SetTone(Value, tone);
            }
        }

        public static KeyValueRow KeyValue(string key, string value, Tone tone = Tone.None, string tooltip = null) =>
            new KeyValueRow(key, value, tone, tooltip);

        // A fact whose value is not text, such as a chip row. The caller keeps content to refill it.
        public static VisualElement KeyValue(string key, VisualElement content, string tooltip = null)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("ab-kv");
            var k = new Label(key);
            k.AddToClassList("ab-kv-key");
            row.Add(k);
            content.AddToClassList("ab-grow");
            row.Add(content);
            return row;
        }

        // Swaps the .ab-text-* colour class on any element; Tone.None clears it.
        public static void SetTone(VisualElement element, Tone tone)
        {
            foreach (var c in TextClasses)
            {
                element.RemoveFromClassList(c);
            }
            string suffix = Suffix(tone);
            if (suffix != null)
            {
                element.AddToClassList("ab-text-" + suffix);
            }
        }

        static readonly string[] TextClasses =
            { "ab-text-info", "ab-text-good", "ab-text-warn", "ab-text-bad", "ab-text-muted" };

        static string Suffix(Tone tone)
        {
            switch (tone)
            {
                case Tone.Info: return "info";
                case Tone.Good: return "good";
                case Tone.Warn: return "warn";
                case Tone.Bad: return "bad";
                case Tone.Muted: return "muted";
                default: return null;
            }
        }

        // --------------------------------------------------------------- chips ----

        // A Label, or a Button when onClick is set. A zero-count filter chip is disabled with
        // SetEnabled(false), which dims it and stops its hover.
        public static VisualElement Chip(string text, Tone tone, bool on = false, Action onClick = null,
            string tooltip = null)
        {
            TextElement chip = onClick == null ? new Label(text) : (TextElement)new Button(onClick) { text = text };
            chip.AddToClassList("ab-chip");
            chip.AddToClassList("ab-chip-" + (Suffix(tone) ?? "muted"));
            if (on)
            {
                chip.AddToClassList("ab-chip-on");
            }
            if (onClick != null)
            {
                chip.AddToClassList("ab-chip-click");
            }
            chip.tooltip = tooltip;
            return chip;
        }

        // Categorical, series 1..4.
        public static Label Tag(string text, int series, string tooltip = null)
        {
            var tag = new Label(text) { tooltip = tooltip };
            tag.AddToClassList("ab-chip");
            tag.AddToClassList("ab-tag-" + Mathf.Clamp(series, 1, 4));
            return tag;
        }

        public static Label BetaTag()
        {
            var tag = (Label)Chip("BETA", Tone.Warn,
                tooltip: "Confirmed working in game, but on limited evidence: turn it on deliberately and test.");
            tag.AddToClassList("ab-chip-strong");
            return tag;
        }

        // A wrapping row of chips.
        public static VisualElement Chips(params VisualElement[] chips) => Container("ab-chips", chips);

        // ------------------------------------------------------------- buttons ----

        public static Button Btn(string text, Action onClick, string tooltip = null,
            ButtonKind kind = ButtonKind.Secondary)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList("ab-btn");
            if (kind == ButtonKind.Strong)
            {
                button.AddToClassList("ab-btn-strong");
            }
            else if (kind == ButtonKind.Danger)
            {
                button.AddToClassList("ab-btn-danger");
            }
            return button;
        }

        // A text-only action in place: More, clear, Show all.
        public static Button TextLink(string text, Action onClick, string tooltip = null)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList("ab-link");
            return button;
        }

        // Callers never type the arrow themselves, so every external link carries exactly one.
        // The Action form is for an address worked out on click, such as a pre-filled issue.
        public static Button ExternalLink(string text, Action open, string tooltip = null) =>
            TextLink(text + " ↗", open, tooltip);

        public static Button ExternalLink(string text, string url, string tooltip = null) =>
            ExternalLink(text, () => Application.OpenURL(url), tooltip ?? url);

        // The one button a view exists for. The gradient means "this is the crossing" and belongs
        // to the converter alone; solid is every other window's main action.
        public class PrimaryButton : Button
        {
            readonly Label _label;
            readonly Color? _solid;

            public PrimaryButton(string text, Action onClick, Color? solid = null) : base(onClick)
            {
                AddToClassList("ab-primary");
                _solid = solid;
                if (solid.HasValue)
                {
                    AddToClassList("ab-primary-solid");
                }
                Fill(true);

                // Hover and press darken through this overlay in USS, under the label, so the
                // label's contrast only rises. C# hover handlers used to stick bright.
                var sheen = new VisualElement { pickingMode = PickingMode.Ignore };
                sheen.AddToClassList("ab-primary-sheen");
                Add(sheen);

                _label = new Label(text) { pickingMode = PickingMode.Ignore };
                Add(_label);
            }

            public void SetLabel(string text) => _label.text = text;

            public void SetActive(bool value)
            {
                SetEnabled(value);
                Fill(value);
            }

            // Inline beats USS, so the brand fill comes off while disabled. Faded brand colour
            // read as a danger tint; :disabled in USS paints a neutral slab instead.
            void Fill(bool on)
            {
                if (_solid.HasValue)
                {
                    style.backgroundColor = on ? new StyleColor(_solid.Value) : new StyleColor(StyleKeyword.Null);
                }
                else
                {
                    style.backgroundImage = on
                        ? new StyleBackground(BridgeTheme.Gradient(BridgeTheme.Span.Bridge))
                        : new StyleBackground(StyleKeyword.Null);
                }
            }
        }

        // ---------------------------------------------------------------- rows ----

        // No wrap. Children after the first get .ab-after, since USS has no sibling combinator.
        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("ab-row");
            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }
                if (row.childCount > 0)
                {
                    child.AddToClassList("ab-after");
                }
                row.Add(child);
            }
            return row;
        }

        public static VisualElement ButtonRow(params VisualElement[] children) => Container("ab-btn-row", children);

        // Controls along the top of a card; an .ab-grow child takes the rest.
        public static VisualElement ToolbarRow(params VisualElement[] children) => Container("ab-toolbar", children);

        public static VisualElement Indent(params VisualElement[] children) => Container("ab-indent", children);

        static VisualElement Container(string className, VisualElement[] children)
        {
            var box = new VisualElement();
            box.AddToClassList(className);
            foreach (var child in children)
            {
                if (child != null)
                {
                    box.Add(child);
                }
            }
            return box;
        }

        // -------------------------------------------------------------- fields ----

        public static Toggle Bind(string label, string tooltip, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value, tooltip = tooltip };
            toggle.AddToClassList("ab-toggle");
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            return toggle;
        }

        // One question with a few answers, as radio buttons under a short label.
        public static RadioButtonGroup Choice(string label, string tooltip, string[] answers, int current,
            Action<int> set)
        {
            var group = new RadioButtonGroup(label, new List<string>(answers))
            {
                value = current,
                tooltip = tooltip,
            };
            group.AddToClassList("ab-field");
            group.AddToClassList("ab-radio");
            group.RegisterValueChangedCallback(e => set(e.newValue));
            return group;
        }

        // EnumField has no formatting callbacks in 2022.3 and shows raw identifiers, so an enum
        // popup is spelled-out labels and an index.
        public static PopupField<string> Popup(string label, string tooltip, string[] choices, int current,
            Action<int> set)
        {
            var popup = new PopupField<string>(label, new List<string>(choices),
                Mathf.Clamp(current, 0, Mathf.Max(0, choices.Length - 1))) { tooltip = tooltip };
            popup.AddToClassList("ab-field");
            popup.RegisterValueChangedCallback(_ => set(popup.index));
            return popup;
        }

        public static TextField Text(string label, string tooltip, string value, Action<string> set)
        {
            var field = new TextField(label) { value = value ?? string.Empty, tooltip = tooltip };
            field.AddToClassList("ab-field");
            field.RegisterValueChangedCallback(e => set(e.newValue));
            return field;
        }

        public static IntegerField IntField(string label, string tooltip, int value, Action<int> set)
        {
            var field = new IntegerField(label) { value = value, tooltip = tooltip };
            field.AddToClassList("ab-field");
            field.RegisterValueChangedCallback(e => set(e.newValue));
            return field;
        }

        // set always runs a frame later: every picker in the repo rebuilds the window.
        public static ObjectField ObjectPicker<T>(string label, T value, Action<T> set, string tooltip = null,
            bool sceneObjects = true) where T : UnityEngine.Object
        {
            var field = new ObjectField(label)
            {
                objectType = typeof(T),
                allowSceneObjects = sceneObjects,
                value = value,
                tooltip = tooltip,
            };
            field.AddToClassList("ab-field");
            field.RegisterValueChangedCallback(e =>
            {
                var picked = e.newValue as T;
                Defer(field, () => set(picked));
            });
            return field;
        }

        // Not "Slider": a method named like the type breaks the type name inside this class.
        public static Slider SliderField(string label, string tooltip, float min, float max, float value,
            Action<float> set)
        {
            var slider = new Slider(label, min, max) { value = value, tooltip = tooltip, showInputField = true };
            slider.AddToClassList("ab-field");
            slider.AddToClassList("ab-slider");
            // Unity marks the groove DynamicColor, the slider's only such part. In the offscreen
            // renders it drew a colour from neither stylesheet and skipped the Play-mode tint the
            // knob took, so the token never showed. Drawn like the rest, it takes the stylesheet.
            var groove = slider.Q(className: Slider.trackerUssClassName);
            if (groove != null)
            {
                groove.usageHints = UsageHints.None;
            }
            slider.RegisterValueChangedCallback(e => set(e.newValue));
            return slider;
        }

        // A live field over a serialized property; the caller calls Bind(so) on the container.
        // Null when this version of the component lacks the property, so callers skip it. Bind
        // to leaf properties so a custom IMGUI drawer never takes over.
        public static VisualElement Bound(SerializedObject so, string path, string label, string tooltip = null)
        {
            var property = so.FindProperty(path);
            if (property == null)
            {
                return null;
            }
            if (property.propertyType == SerializedPropertyType.Boolean)
            {
                var toggle = new Toggle(label) { bindingPath = property.propertyPath, tooltip = tooltip };
                toggle.AddToClassList("ab-toggle");
                return toggle;
            }
            // Numbers get their field directly: a PropertyField builds its control only once the
            // binding runs, so until then it is an empty row.
            VisualElement field = property.propertyType switch
            {
                SerializedPropertyType.Float => new FloatField(label) { bindingPath = property.propertyPath, tooltip = tooltip },
                SerializedPropertyType.Integer => new IntegerField(label) { bindingPath = property.propertyPath, tooltip = tooltip },
                _ => new PropertyField(property, label) { tooltip = tooltip },
            };
            field.AddToClassList("ab-field");
            return field;
        }

        // Read-only but selectable, so names stay copyable without looking editable.
        public static TextField Code(string text)
        {
            var field = new TextField { value = text ?? string.Empty, isReadOnly = true, multiline = true };
            field.AddToClassList("ab-code");
            return field;
        }

        // -------------------------------------------------------------- notice ----

        // A tinted box with an icon; replaces HelpBox. Kept under the button row it reports on,
        // it is also that action's status line.
        public sealed class NoticeBox : VisualElement
        {
            static readonly string[] ToneClasses =
                { "ab-notice-info", "ab-notice-good", "ab-notice-warn", "ab-notice-bad" };

            readonly VisualElement _icon;
            readonly Label _text;
            readonly VisualElement _body;

            internal NoticeBox(Tone tone, string text, string more, Button action)
            {
                AddToClassList("ab-notice");
                _icon = new VisualElement { pickingMode = PickingMode.Ignore };
                _icon.AddToClassList("ab-notice-icon");
                Add(_icon);

                _body = new VisualElement();
                _body.AddToClassList("ab-notice-body");
                Add(_body);

                _text = new Label();
                _text.AddToClassList("ab-notice-text");
                _body.Add(_text);

                if (!string.IsNullOrEmpty(more))
                {
                    var rest = new Label(more);
                    rest.AddToClassList("ab-notice-text");
                    AddMore(_body, rest);
                }
                if (action != null)
                {
                    action.AddToClassList("ab-notice-action");
                    _body.Add(action);
                }
                Set(text, tone);
            }

            public void Set(string text, Tone tone)
            {
                _text.text = text ?? string.Empty;
                foreach (var c in ToneClasses)
                {
                    RemoveFromClassList(c);
                }
                string suffix = tone == Tone.Good ? "good" : tone == Tone.Warn ? "warn" : tone == Tone.Bad ? "bad" : "info";
                // The icon comes from USS by skin and tone: one picked here kept the old skin's
                // glyph after a switch, white on the light info tint.
                AddToClassList("ab-notice-" + suffix);
                RemoveFromClassList("ab-hidden");
            }

            public void Hide() => AddToClassList("ab-hidden");
        }

        public static NoticeBox Notice(Tone tone, string text, string more = null, Button action = null) =>
            new NoticeBox(tone, text, more, action);

        // In place of disabling a card: the explanation must never be dimmed.
        public static VisualElement Empty(string title, string text, Button action = null)
        {
            var box = new VisualElement();
            box.AddToClassList("ab-empty");
            var t = new Label(title);
            t.AddToClassList("ab-empty-title");
            box.Add(t);
            if (!string.IsNullOrEmpty(text))
            {
                var body = new Label(text);
                body.AddToClassList("ab-empty-text");
                box.Add(body);
            }
            if (action != null)
            {
                action.AddToClassList("ab-empty-action");
                box.Add(action);
            }
            return box;
        }

        // -------------------------------------------------------------- search ----

        // Show a search box when a scope has more than this many rows.
        public const int SearchFrom = 10;

        // Filters each scope's Body. A scope with no match is hidden; one with a match opens for
        // the length of the query and goes back to how it was when the box clears. There is no
        // placeholder in 2022.3, so the caller's tooltip says what it searches.
        public static ToolbarSearchField SearchField(params Card[] scopes)
        {
            var field = new ToolbarSearchField();
            field.AddToClassList("ab-search");
            field.AddToClassList("ab-keep");
            Dictionary<Card, bool> was = null;
            field.RegisterValueChangedCallback(e =>
            {
                bool searching = !string.IsNullOrWhiteSpace(e.newValue);
                if (searching && was == null)
                {
                    was = new Dictionary<Card, bool>();
                    foreach (var scope in scopes)
                    {
                        was[scope] = scope.Open;
                    }
                }
                foreach (var scope in scopes)
                {
                    bool titled = searching && scope.Title.ToLowerInvariant().Contains(e.newValue.Trim().ToLowerInvariant());
                    bool match = Filter(scope.Body, titled ? null : e.newValue) || titled;
                    // Never hide the scope holding the box, or the query could not be cleared.
                    scope.EnableInClassList("ab-filtered-out", searching && !match && !scope.Contains(field));
                    if (searching && match)
                    {
                        scope.Open = true;
                    }
                }
                if (!searching && was != null)
                {
                    foreach (var kv in was)
                    {
                        kv.Key.Open = kv.Value;
                    }
                    was = null;
                }
            });
            return field;
        }

        // Hides what does not match, by class, so clearing never shows anything a window hid on
        // purpose. A setting matches on its label or its explanation: people look for what a
        // setting does, not its name. A heading that matches shows its whole group, and one left
        // with nothing under it hides. True when anything still shows.
        public static bool Filter(VisualElement container, string query)
        {
            bool all = string.IsNullOrWhiteSpace(query);
            string q = all ? "" : query.Trim().ToLowerInvariant();
            bool any = false;

            VisualElement heading = null;
            bool headingMatch = false, headingUsed = false;
            foreach (var child in container.Children())
            {
                // The box being typed into lives in what it filters.
                if (child.ClassListContains("ab-keep")) continue;

                if (child is Label && child.ClassListContains("ab-section"))
                {
                    any |= CloseGroup(heading, headingUsed);
                    heading = child;
                    headingMatch = !all && Describes(child).Contains(q);
                    headingUsed = all || headingMatch;
                    continue;
                }

                bool show = all || headingMatch || Describes(child).Contains(q);
                child.EnableInClassList("ab-filtered-out", !show);
                if (show)
                {
                    headingUsed = true;
                    any = true;
                }
            }
            any |= CloseGroup(heading, headingUsed);
            return any;
        }

        static bool CloseGroup(VisualElement heading, bool used)
        {
            if (heading == null) return false;
            heading.EnableInClassList("ab-filtered-out", !used);
            return used;
        }

        static string Describes(VisualElement element)
        {
            var text = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(element.tooltip)) text.Append(element.tooltip).Append(' ');
            foreach (var label in element.Query<Label>().Build())
            {
                text.Append(label.text).Append(' ');
            }
            if (element is Toggle toggle) text.Append(toggle.label).Append(' ');
            return text.ToString().ToLowerInvariant();
        }

        // ----------------------------------------------------------- segmented ----

        // Pick one of a few. The live state comes from SetCurrent; the caller must not rebuild
        // synchronously in onPick.
        public sealed class Segmented : VisualElement
        {
            readonly List<Button> _items = new List<Button>();

            public Segmented(string[] labels, int current, Action<int> onPick, string[] tooltips = null)
            {
                AddToClassList("ab-seg");
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    var item = new Button(() =>
                    {
                        SetCurrent(index);
                        onPick?.Invoke(index);
                    })
                    {
                        text = labels[i],
                        tooltip = tooltips != null && i < tooltips.Length ? tooltips[i] : labels[i],
                    };
                    item.AddToClassList("ab-seg-item");
                    // The hairline between items; USS has no sibling combinator.
                    item.EnableInClassList("ab-after", i > 0);
                    _items.Add(item);
                    Add(item);
                }
                SetCurrent(current);
            }

            // -1 = none selected. Never fires onPick.
            public void SetCurrent(int index)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    _items[i].EnableInClassList("ab-on", i == index);
                }
            }
        }

        // ------------------------------------------------------------- report ----

        public static VisualElement ReportRow(Tone tone, string head, string detail = null,
            params VisualElement[] actions)
        {
            var row = new VisualElement();
            row.AddToClassList("ab-report-item");

            var stripe = new VisualElement();
            stripe.AddToClassList("ab-report-stripe");
            stripe.AddToClassList("ab-stripe-" + (Suffix(tone) ?? "muted"));
            row.Add(stripe);

            var text = new VisualElement();
            text.AddToClassList("ab-report-text");
            var title = new Label(head);
            title.AddToClassList("ab-report-head");
            text.Add(title);
            if (!string.IsNullOrEmpty(detail))
            {
                var body = new Label(detail);
                body.AddToClassList("ab-report-detail");
                text.Add(body);
            }
            row.Add(text);

            if (actions != null && actions.Length > 0)
            {
                row.Add(Container("ab-report-actions", actions));
            }
            return row;
        }

        // Head is "Category: Subject", never the status word.
        public static VisualElement ReportRow(ReportEntry e, params VisualElement[] actions) =>
            ReportRow(BridgeTheme.ToneOf(e.Status),
                string.IsNullOrEmpty(e.Subject) ? e.Category : e.Category + ": " + e.Subject,
                e.Detail, actions);

        // No inner scroller, which trapped the wheel: past initial rows a "Show all N" link
        // appends the rest in place. The list alternates row tints itself.
        public static VisualElement ReportList(IList<VisualElement> rows, int initial = 30)
        {
            var list = new VisualElement();
            list.AddToClassList("ab-report-list");
            int first = initial <= 0 ? rows.Count : Math.Min(initial, rows.Count);
            for (int i = 0; i < first; i++)
            {
                AddReportRow(list, rows[i], i);
            }
            if (rows.Count > first)
            {
                var more = new VisualElement();
                more.AddToClassList("ab-report-more");
                more.Add(TextLink($"Show all {rows.Count}", () => Defer(list, () =>
                {
                    list.Remove(more);
                    for (int i = first; i < rows.Count; i++)
                    {
                        AddReportRow(list, rows[i], i);
                    }
                })));
                list.Add(more);
            }
            return list;
        }

        static void AddReportRow(VisualElement list, VisualElement row, int index)
        {
            row.EnableInClassList("ab-row-alt", index % 2 == 1);
            list.Add(row);
        }
    }
}
