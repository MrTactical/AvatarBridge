using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    /// <summary>
    /// The window's building blocks.
    ///
    /// Elements are constructed in C# rather than declared in UXML on purpose: UXML would put the
    /// layout in one file and the wiring in another, joined by string names looked up with Q&lt;&gt;,
    /// and a typo in that join fails silently at runtime. Built here, construction and wiring are
    /// the same line of code, and the compiler checks it. The stylesheet still carries everything
    /// USS is actually better at — the cascade, :hover, and the two skins.
    /// </summary>
    internal static class BridgeElements
    {
        /// <summary>
        /// The title bar: the blue-to-orange bridge, with the crossing named on it.
        /// </summary>
        public static VisualElement Banner(string title, string subtitle, string version)
        {
            var bar = new VisualElement();
            bar.AddToClassList("ab-banner");
            bar.style.backgroundImage = new StyleBackground(BridgeTheme.BridgeGradient());

            var text = new VisualElement();
            text.AddToClassList("ab-banner-text");

            var name = new Label(title);
            name.AddToClassList("ab-banner-title");
            var sub = new Label(subtitle);
            sub.AddToClassList("ab-banner-sub");
            text.Add(name);
            text.Add(sub);
            bar.Add(text);

            if (!string.IsNullOrEmpty(version))
            {
                var pill = new Label(version);
                pill.AddToClassList("ab-version");
                bar.Add(pill);
            }
            return bar;
        }

        /// <summary>
        /// A tab strip in the CCK's shape — icon plus label in a rounded, bordered container.
        /// </summary>
        public static VisualElement Tabs(string[] labels, string[] icons, int current,
            Action<int> onSelect)
        {
            var strip = new VisualElement();
            strip.AddToClassList("ab-tabs");

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var tab = new VisualElement();
                tab.AddToClassList("ab-tab");
                if (i == current)
                {
                    tab.AddToClassList("ab-tab-on");
                    // The active tab is tinted to its own end of the bridge, so switching modes
                    // moves you along it rather than just highlighting a button.
                    tab.style.backgroundImage = new StyleBackground(
                        BridgeTheme.Solid(BridgeTheme.At(labels.Length == 1 ? 0f : i / (float)(labels.Length - 1))));
                }

                var icon = BridgeTheme.GetIcon(icons != null && index < icons.Length ? icons[index] : null);
                if (icon != null)
                {
                    var image = new VisualElement();
                    image.AddToClassList("ab-tab-icon");
                    image.style.backgroundImage = new StyleBackground((Texture2D)icon);
                    tab.Add(image);
                }
                tab.Add(new Label(labels[i]));
                tab.RegisterCallback<MouseDownEvent>(_ => onSelect(index));
                strip.Add(tab);
            }
            return strip;
        }

        /// <summary>
        /// A card. <paramref name="step"/> adds a numbered badge tinted to its position on the
        /// bridge; <paramref name="expanded"/> null means the card doesn't collapse.
        /// </summary>
        public class Card : VisualElement
        {
            public readonly VisualElement Body = new VisualElement();
            readonly Label _summary;
            readonly VisualElement _arrow;
            bool _open = true;

            public Card(string title, string summary = null, bool? expanded = null,
                int? step = null, float bridgePosition = 0f, Action<bool> onToggle = null)
            {
                AddToClassList("ab-card");
                Body.AddToClassList("ab-card-body");

                var header = new VisualElement();
                header.AddToClassList("ab-card-header");

                if (step.HasValue)
                {
                    var badge = new VisualElement();
                    badge.AddToClassList("ab-step-badge");
                    badge.style.backgroundImage =
                        new StyleBackground(BridgeTheme.Solid(BridgeTheme.At(bridgePosition)));
                    badge.Add(new Label(step.Value.ToString()));
                    header.Add(badge);
                }

                if (expanded.HasValue)
                {
                    AddToClassList("ab-card-collapsible");
                    _open = expanded.Value;
                    _arrow = new VisualElement();
                    _arrow.AddToClassList("ab-card-arrow");
                    header.Add(_arrow);
                }

                var label = new Label(title);
                label.AddToClassList("ab-card-title");
                header.Add(label);

                _summary = new Label(summary ?? string.Empty);
                _summary.AddToClassList("ab-card-summary");
                _summary.style.color = BridgeTheme.Muted;
                _summary.style.display = string.IsNullOrEmpty(summary)
                    ? DisplayStyle.None : DisplayStyle.Flex;
                header.Add(_summary);

                Add(header);
                Add(Body);
                Apply();

                if (expanded.HasValue)
                {
                    header.RegisterCallback<MouseDownEvent>(_ =>
                    {
                        _open = !_open;
                        Apply();
                        onToggle?.Invoke(_open);
                    });
                }
            }

            /// <summary>Updates the right-hand summary — the current setting, so a collapsed
            /// card still says where it stands.</summary>
            public void SetSummary(string text)
            {
                _summary.text = text ?? string.Empty;
                _summary.style.display = string.IsNullOrEmpty(text)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }

            void Apply()
            {
                Body.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
                if (_arrow == null)
                {
                    return;
                }
                _arrow.EnableInClassList("ab-card-arrow-on", _open);
            }
        }

        /// <summary>The one button the window exists for, filled with the bridge itself.</summary>
        public class PrimaryButton : VisualElement
        {
            readonly Label _label;

            public PrimaryButton(string text, Action onClick)
            {
                AddToClassList("ab-primary");
                style.backgroundImage = new StyleBackground(BridgeTheme.BridgeGradient());
                _label = new Label(text);
                Add(_label);

                // A disabled VisualElement stops receiving events, so SetEnabled is the guard;
                // the class only supplies the look, since a bare element has no disabled styling.
                RegisterCallback<MouseDownEvent>(_ => onClick());
                RegisterCallback<MouseEnterEvent>(_ =>
                    style.backgroundImage = new StyleBackground(BridgeTheme.BridgeGradient(true)));
                RegisterCallback<MouseLeaveEvent>(_ =>
                    style.backgroundImage = new StyleBackground(BridgeTheme.BridgeGradient()));
            }

            public void SetLabel(string text) => _label.text = text;

            public void SetActive(bool value)
            {
                SetEnabled(value);
                EnableInClassList("ab-primary-off", !value);
            }
        }

        /// <summary>A tally chip. Only non-zero counts are emphasised, so a clean run reads clean.</summary>
        public static Label Chip(string text, Color colour, bool emphasise)
        {
            var chip = new Label(text);
            chip.AddToClassList("ab-chip");
            chip.style.backgroundColor = new Color(colour.r, colour.g, colour.b, emphasise ? 0.22f : 0.10f);
            chip.style.color = emphasise ? colour : BridgeTheme.Muted;
            return chip;
        }

        /// <summary>Marks a toggle as opt-in rather than broken.</summary>
        public static Label BetaTag()
        {
            var tag = new Label("BETA");
            tag.AddToClassList("ab-beta");
            tag.style.backgroundColor = new Color(BridgeTheme.Warn.r, BridgeTheme.Warn.g, BridgeTheme.Warn.b, 0.18f);
            tag.style.color = BridgeTheme.Warn;
            tag.tooltip = "Confirmed working in game, but on limited evidence — turn it on deliberately and test.";
            return tag;
        }

        // ------------------------------------------------------------ controls ----

        /// <summary>
        /// A checkbox bound to a setting. The callback is the whole point: every control has to
        /// write back, and a toggle that displays but never saves is the failure this shape of
        /// helper exists to prevent.
        /// </summary>
        public static Toggle Bind(string label, string tooltip, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value, tooltip = tooltip };
            toggle.AddToClassList("ab-toggle");
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            return toggle;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("ab-row");
            foreach (var child in children)
            {
                row.Add(child);
            }
            return row;
        }

        public static Label SubHeading(string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.AddToClassList("ab-sub");
            label.style.color = BridgeTheme.Muted;
            return label;
        }

        public static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ab-hint");
            label.style.color = BridgeTheme.Muted;
            return label;
        }
    }
}
