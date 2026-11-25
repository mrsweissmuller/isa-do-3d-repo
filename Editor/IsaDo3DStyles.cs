using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace IsaDo3D.EnvironmentArt
{
    // O visual das janelas do pacote, num lugar só. As janelas puxam com using static
    internal static class IsaDo3DStyles
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

        // Não pode chamar Show, a EditorWindow já tem um
        public static void SetVisible(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

        // Subtítulo dentro de um cartão
        public static Label Section(string text)
        {
            var section = new Label(text);
            section.style.fontSize = 11f;
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.color = MutedColor;
            section.style.marginBottom = 4f;
            return section;
        }

        // Fundo da janela e rolagem, devolve onde entram o cabeçalho e os cartões
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

        // Nome da ferramenta em cima da faixa colorida
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

        // Cartão com título à esquerda e uma observação curta à direita
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
            return button;
        }

        // Ação secundária que não merece um botão cheio
        public static Button LinkButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.backgroundColor = Color.clear;
            button.style.color = Dark ? Hex(0xC9D8FF) : Hex(0x1F3F99);
            button.style.fontSize = 11f;
            Border(button, 0f, Color.clear);
            Margin(button, 0f);
            Padding(button, 0f, 4f);
            return button;
        }
    }

    // Escolha de uma opção entre várias, com a cara das abas do Hierarchy Organizer
    internal class SegmentedBar : VisualElement
    {
        private readonly VisualElement[] _segments;
        private readonly Label[] _labels;

        public SegmentedBar(string[] options, Action<int> onSelect)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.backgroundColor = IsaDo3DStyles.InsetColor;
            IsaDo3DStyles.Radius(this, 7f);
            IsaDo3DStyles.Padding(this, 3f, 3f);

            _segments = new VisualElement[options.Length];
            _labels = new Label[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;

                var segment = IsaDo3DStyles.Row();
                segment.style.flexGrow = 1f;
                segment.style.flexBasis = 0f;
                segment.style.height = 24f;
                segment.style.justifyContent = Justify.Center;
                IsaDo3DStyles.Radius(segment, 5f);
                segment.AddManipulator(new Clickable(() => onSelect(index)));

                var label = new Label(options[i]);
                label.pickingMode = PickingMode.Ignore;
                segment.Add(label);

                _segments[i] = segment;
                _labels[i] = label;
                Add(segment);
            }
        }

        // Só pinta, quem guarda a escolha é a janela
        public void Select(int selected)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                bool active = i == selected;
                _segments[i].style.backgroundColor = active ? IsaDo3DStyles.Accent : Color.clear;
                _labels[i].style.color = active ? Color.white : IsaDo3DStyles.MutedColor;
                _labels[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }
}
