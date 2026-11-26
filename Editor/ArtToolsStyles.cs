using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtTools.EnvironmentArt
{
    internal static class ArtToolsStyles
    {
        public static Color Hex(int hex, float alpha = 1f)
        {
            return new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, alpha);
        }

        public static bool Dark => EditorGUIUtility.isProSkin;
        public static Color WindowColor => Dark ? Hex(0x2A2A2A) : Hex(0xC8C8C8);
        public static Color CardColor => Dark ? Hex(0x383838) : Hex(0xDEDEDE);
        public static Color CardBorder => Dark ? Hex(0x4A4A4A) : Hex(0xAFAFAF);
        public static Color InsetColor => Dark ? Hex(0x2B2B2B) : Hex(0xC6C6C6);
        public static Color ControlColor => Dark ? Hex(0x4D4D4D) : Hex(0xECECEC);
        public static Color TextColor => Dark ? Hex(0xE4E4E4) : Hex(0x1A1A1A);
        public static Color MutedColor => Dark ? Hex(0x9A9A9A) : Hex(0x555555);
        public static readonly Color Accent = Hex(0x4C7EF3);
        public static readonly Color Danger = Hex(0xD9534F);

        private static readonly Color[] StripColors =
        {
            Hex(0x3D64C7), Hex(0x4B8F27), Hex(0xBB7723), Hex(0x7137A5), Hex(0xB85B88),
            Hex(0x539BA5), Hex(0xB74843), Hex(0xB1992F),
        };

        public static void Radius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        public static void Padding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
        }

        public static void Margin(VisualElement element, float value)
        {
            element.style.marginTop = value;
            element.style.marginBottom = value;
            element.style.marginLeft = value;
            element.style.marginRight = value;
        }

        public static void SetVisible(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Color Hovered(Color resting)
        {
            if (resting.a < 0.05f) return Dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f);
            return Dark ? Color.Lerp(resting, Color.white, 0.14f) : Color.Lerp(resting, Color.black, 0.10f);
        }

        public static void HoverFeedback(VisualElement element)
        {
            Color resting = default;
            Color hovered = default;
            bool hovering = false;

            element.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!element.enabledInHierarchy) return;

                resting = element.style.backgroundColor.value;
                hovered = Hovered(resting);
                element.style.backgroundColor = hovered;
                hovering = true;
            });

            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!hovering) return;
                hovering = false;

                if (element.style.backgroundColor.value == hovered) element.style.backgroundColor = resting;
            });
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static Label Note(string text)
        {
            var note = new Label(text);
            note.style.fontSize = 11f;
            note.style.color = MutedColor;
            note.style.whiteSpace = WhiteSpace.Normal;
            return note;
        }

        public static Label Section(string text)
        {
            var section = new Label(text);
            section.style.fontSize = 11f;
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.color = MutedColor;
            section.style.marginBottom = 4f;
            return section;
        }

        public static VisualElement Page(VisualElement root)
        {
            root.Clear();
            root.style.backgroundColor = WindowColor;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            root.Add(scroll);

            var page = scroll.contentContainer;
            Padding(page, 10f, 10f);
            return page;
        }

        public static VisualElement Header(string title)
        {
            var header = new VisualElement();
            header.style.marginBottom = 10f;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 15f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = TextColor;
            titleLabel.style.marginBottom = 6f;
            header.Add(titleLabel);

            var strip = Row();
            strip.style.height = 2f;
            foreach (var color in StripColors)
            {
                var segment = new VisualElement();
                segment.style.flexGrow = 1f;
                segment.style.height = 2f;
                segment.style.backgroundColor = color;
                strip.Add(segment);
            }
            header.Add(strip);

            return header;
        }

        public static VisualElement Card(string title)
        {
            return Card(title, "", out _);
        }

        public static VisualElement Card(string title, string note, out Label noteLabel)
        {
            var card = new VisualElement();
            card.style.backgroundColor = CardColor;
            card.style.marginBottom = 8f;
            Radius(card, 8f);
            Border(card, 1f, CardBorder);
            Padding(card, 10f, 12f);

            var header = Row();
            header.style.marginBottom = 8f;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 13f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = TextColor;
            titleLabel.style.flexGrow = 1f;
            header.Add(titleLabel);

            noteLabel = new Label(note);
            noteLabel.style.fontSize = 10f;
            noteLabel.style.color = MutedColor;
            header.Add(noteLabel);

            card.Add(header);
            return card;
        }

        public static VisualElement Tile(Color accent, out VisualElement content)
        {
            var tile = Row();
            tile.style.alignItems = Align.Stretch;
            tile.style.backgroundColor = InsetColor;
            tile.style.marginBottom = 6f;
            tile.style.overflow = Overflow.Hidden;
            Radius(tile, 6f);

            var strip = new VisualElement();
            strip.style.width = 3f;
            strip.style.backgroundColor = accent;
            tile.Add(strip);

            content = new VisualElement();
            content.style.flexGrow = 1f;
            content.style.flexBasis = 0f;
            Padding(content, 6f, 8f);
            tile.Add(content);

            return tile;
        }

        public static void SetTileAccent(VisualElement tile, Color accent)
        {
            tile[0].style.backgroundColor = accent;
        }

        public static Label TileTitle(string text)
        {
            var title = new Label(text);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = TextColor;
            title.style.marginBottom = 4f;
            return title;
        }

        public static Button FlatButton(string text, bool primary, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.backgroundColor = primary ? Accent : ControlColor;
            button.style.color = primary ? Color.white : TextColor;
            if (primary) button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.height = primary ? 26f : 22f;
            Border(button, 0f, Color.clear);
            Radius(button, 5f);
            Margin(button, 0f);
            HoverFeedback(button);
            return button;
        }

        public static Button LinkButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.backgroundColor = Color.clear;
            button.style.color = Dark ? Hex(0xC9D8FF) : Hex(0x1F3F99);
            button.style.fontSize = 11f;
            Border(button, 0f, Color.clear);
            Margin(button, 0f);
            Padding(button, 0f, 4f);
            Radius(button, 4f);
            HoverFeedback(button);
            return button;
        }
    }

    internal class SegmentedBar : VisualElement
    {
        private readonly VisualElement[] _segments;
        private readonly Label[] _labels;

        public SegmentedBar(string[] options, Action<int> onSelect)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.backgroundColor = ArtToolsStyles.InsetColor;
            ArtToolsStyles.Radius(this, 7f);
            ArtToolsStyles.Padding(this, 3f, 3f);

            _segments = new VisualElement[options.Length];
            _labels = new Label[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;

                var segment = ArtToolsStyles.Row();
                segment.style.flexGrow = 1f;
                segment.style.flexBasis = 0f;
                segment.style.height = 24f;
                segment.style.justifyContent = Justify.Center;
                ArtToolsStyles.Radius(segment, 5f);
                segment.AddManipulator(new Clickable(() => onSelect(index)));
                ArtToolsStyles.HoverFeedback(segment);

                var label = new Label(options[i]);
                label.pickingMode = PickingMode.Ignore;
                segment.Add(label);

                _segments[i] = segment;
                _labels[i] = label;
                Add(segment);
            }
        }

        public void Select(int selected)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                bool active = i == selected;
                _segments[i].style.backgroundColor = active ? ArtToolsStyles.Accent : Color.clear;
                _labels[i].style.color = active ? Color.white : ArtToolsStyles.MutedColor;
                _labels[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }

    internal class DropdownPill : VisualElement
    {
        private readonly List<string> _choices;
        private readonly Label _text;
        private Func<string, string> _menuLabel;
        private Func<string, string> _fieldLabel;

        public string value { get; private set; }

        public event Action<string> Changed;

        public bool AlwaysNotify;

        public Func<string, string> MenuLabel
        {
            get => _menuLabel;
            set => _menuLabel = value;
        }

        public Func<string, string> FieldLabel
        {
            get => _fieldLabel;
            set
            {
                _fieldLabel = value;
                RefreshText();
            }
        }

        public DropdownPill(List<string> choices, int index)
        {
            _choices = choices;

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 20f;
            style.backgroundColor = ArtToolsStyles.InsetColor;
            ArtToolsStyles.Border(this, 1f, ArtToolsStyles.CardBorder);
            ArtToolsStyles.Radius(this, 5f);
            ArtToolsStyles.Padding(this, 0f, 7f);

            _text = new Label { pickingMode = PickingMode.Ignore };
            _text.style.flexGrow = 1f;
            _text.style.flexShrink = 1f;
            _text.style.overflow = Overflow.Hidden;
            _text.style.color = ArtToolsStyles.TextColor;
            _text.style.unityTextAlign = TextAnchor.MiddleLeft;
            Add(_text);

            var arrow = new Label("\u25BE") { pickingMode = PickingMode.Ignore };
            arrow.style.color = ArtToolsStyles.MutedColor;
            arrow.style.marginLeft = 4f;
            Add(arrow);

            value = choices.Count > 0 ? choices[Mathf.Clamp(index, 0, choices.Count - 1)] : "";
            RefreshText();

            this.AddManipulator(new Clickable(Open));
            ArtToolsStyles.HoverFeedback(this);
        }

        public void SetValueWithoutNotify(string newValue)
        {
            value = newValue;
            RefreshText();
        }

        private void RefreshText()
        {
            _text.text = _fieldLabel != null ? _fieldLabel(value) : value;
        }

        private void Open()
        {
            var menu = new GenericDropdownMenu();
            foreach (var choice in _choices)
            {
                string picked = choice;
                menu.AddItem(_menuLabel != null ? _menuLabel(choice) : choice, choice == value, () =>
                {
                    if (picked == value && !AlwaysNotify) return;
                    SetValueWithoutNotify(picked);
                    Changed?.Invoke(picked);
                });
            }

            StyleMenu(menu);
#if UNITY_6000_5_OR_NEWER
            menu.DropDown(worldBound, this, DropdownMenuSizeMode.Fixed);
#else
            menu.DropDown(worldBound, this, true);
#endif
        }

        private static void StyleMenu(GenericDropdownMenu menu)
        {
            for (var element = menu.contentContainer; element != null; element = element.parent)
            {
                if (!element.ClassListContains("unity-base-dropdown__container-outer")) continue;

                element.style.backgroundColor = ArtToolsStyles.Dark ? ArtToolsStyles.Hex(0x2F2F2F) : ArtToolsStyles.Hex(0xE6E6E6);
                ArtToolsStyles.Border(element, 1f, ArtToolsStyles.CardBorder);
                ArtToolsStyles.Radius(element, 7f);
                ArtToolsStyles.Padding(element, 4f, 4f);
                break;
            }

            foreach (var item in menu.contentContainer.Children())
            {
                item.style.height = 22f;
                ArtToolsStyles.Radius(item, 4f);

                var label = item.Q<Label>();
                if (label != null) label.style.color = ArtToolsStyles.TextColor;
            }
        }
    }
}
