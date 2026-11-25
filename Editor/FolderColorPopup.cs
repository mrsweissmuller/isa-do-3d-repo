using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static IsaDo3D.EnvironmentArt.IsaDo3DStyles;

namespace IsaDo3D.EnvironmentArt
{
    // Janelinha que abre no mouse pelo clique direito na pasta: cores pra clicar, cor livre, selos. Tudo vale na
    // hora nas pastas que estavam selecionadas quando ela abriu, e ela fecha com Esc ou clicando fora
    internal class FolderColorPopup : EditorWindow
    {
        private const float Width = 248f;
        private const float SwatchSize = 22f;

        private string[] _guids;
        private VisualElement _box;
        private bool _focusedOnce;

        // Como as pastas estavam antes de cada mudança feita aqui, pro Undo da janela
        private readonly List<Dictionary<string, (string Color, string Badge)>> _history =
            new List<Dictionary<string, (string Color, string Badge)>>();
        private Button _undo;
        private bool _picking;

        internal static void Open(string[] guids, Vector2 screenPosition)
        {
            var popup = CreateInstance<FolderColorPopup>();
            popup._guids = guids;
            popup.position = new Rect(screenPosition.x, screenPosition.y, Width, 230f);
            popup.ShowPopup();
            popup.Focus();
        }

        private void OnFocus() => _focusedOnce = true;

        // ShowAsDropDown fecharia sozinha ao abrir o seletor de cor da Unity, então o fechar é feito aqui
        private void Update()
        {
            if (!_focusedOnce || focusedWindow == this) return;

            var focused = focusedWindow != null ? focusedWindow.GetType().Name : "";
            if (focused == "ColorPicker" || focused == "EyeDropper") return;
            Close();
        }

        private void OnDestroy() => FolderColorState.Flush();

        private void CreateGUI()
        {
            // Depois de recompilar a janela volta sem saber de que pastas era
            if (_guids == null || _guids.Length == 0)
            {
                EditorApplication.delayCall += Close;
                return;
            }

            var root = rootVisualElement;
            root.style.backgroundColor = WindowColor;
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) Close();
            }, TrickleDown.TrickleDown);

            _box = new VisualElement();
            Padding(_box, 10f, 10f);
            Border(_box, 1f, CardBorder);
            _box.RegisterCallback<GeometryChangedEvent>(_ => FitToContent());
            root.Add(_box);

            Rebuild();
            root.Focus();
        }

        // A altura segue o conteúdo, e perto da borda da Unity a janela recua pra caber
        private void FitToContent()
        {
            float height = Mathf.Ceil(_box.layout.height);
            if (float.IsNaN(height) || height < 1f) return;

            var current = position;
            var target = new Rect(current.x, current.y, Width, height);
            var main = EditorGUIUtility.GetMainWindowPosition();
            if (main.Contains(current.position))
            {
                target.x = Mathf.Min(target.x, main.xMax - Width);
                target.y = Mathf.Min(target.y, main.yMax - height);
            }

            if (Mathf.Abs(current.height - height) > 0.5f || target.position != current.position) position = target;
        }

        private void Rebuild()
        {
            _box.Clear();

            var own = FolderColorState.Find(_guids[0]);
            string color = own != null ? own.color : FolderColorState.Inherit;
            string badge = own != null ? own.badge : "";

            var header = Row();
            header.style.marginBottom = 8f;
            var title = new Label("Folder Color");
            title.style.fontSize = 13f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = TextColor;
            title.style.flexGrow = 1f;
            header.Add(title);
            header.Add(Note(_guids.Length == 1
                ? Path.GetFileName(AssetDatabase.GUIDToAssetPath(_guids[0]))
                : $"{_guids.Length} folders"));
            _undo = LinkButton("Undo", UndoLast);
            _undo.tooltip = "Takes back the last change made in this window.";
            _undo.style.marginLeft = 6f;
            _undo.SetEnabled(_history.Count > 0);
            header.Add(_undo);
            _box.Add(header);

            var swatches = WrapRow();
            foreach (var preset in FolderColors.Presets)
                swatches.Add(Swatch(preset.Hex, preset.Name, preset.Hex == color, false));
            foreach (var saved in FolderColorState.Palette)
                swatches.Add(Swatch(saved, $"{saved} (right-click to remove)", saved == color, true));
            _box.Add(swatches);

            var custom = Row();
            custom.style.marginTop = 6f;
            var picker = new ColorField { showAlpha = false, hdr = false, tooltip = "Any color. Applies while you pick." };
            picker.style.flexGrow = 1f;
            picker.style.marginLeft = 2f;
            picker.SetValueWithoutNotify(ColorUtility.TryParseHtmlString(color, out var parsed) ? parsed : Color.white);
            picker.RegisterValueChangedCallback(e => SetColor(ToHex(e.newValue), false));
            custom.Add(picker);
            var save = FlatButton("Save swatch", false, () =>
            {
                // A cor já foi aplicada enquanto a pessoa escolhia
                FolderColorState.AddToPalette(ToHex(picker.value));
                FolderColorState.Flush();
                Later();
            });
            save.tooltip = "Keeps this color in the grid above.";
            custom.Add(save);
            _box.Add(custom);

            var modes = Row();
            modes.style.marginTop = 6f;
            modes.Add(Mode("Same as parent", "Follows the color of the folder above. This is the default.",
                color == FolderColorState.Inherit, FolderColorState.Inherit));
            modes.Add(Mode("No color", "Stays plain even when the folder above has a color.",
                color == FolderColorState.NoColor, FolderColorState.NoColor));
            _box.Add(modes);

            var badgeTitle = Section("Badge");
            badgeTitle.style.marginTop = 10f;
            _box.Add(badgeTitle);

            var badges = WrapRow();
            badges.Add(Badge("", "No badge", badge == ""));
            foreach (var option in FolderColors.Badges)
                badges.Add(Badge(option.Key, option.Name, badge == option.Key));
            _box.Add(badges);
        }

        private static string ToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        // Sem save é o seletor de cor arrastando, e o arrasto inteiro conta como uma mudança só
        private void SetColor(string color, bool save)
        {
            if (save || !_picking) Remember();
            _picking = !save;
            FolderColorState.Edit(_guids, entry => entry.color = color, save);
        }

        private void SetBadge(string badge)
        {
            Remember();
            _picking = false;
            FolderColorState.Edit(_guids, entry => entry.badge = badge);
        }

        private void Remember()
        {
            var before = new Dictionary<string, (string Color, string Badge)>(_guids.Length);
            foreach (var guid in _guids)
            {
                var entry = FolderColorState.Find(guid);
                before[guid] = entry != null ? (entry.color, entry.badge) : (FolderColorState.Inherit, "");
            }
            _history.Add(before);
            _undo?.SetEnabled(true);
        }

        private void UndoLast()
        {
            if (_history.Count == 0) return;

            var before = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            _picking = false;

            FolderColorState.Edit(_guids, entry =>
            {
                entry.color = before[entry.guid].Color;
                entry.badge = before[entry.guid].Badge;
            });
            Later();
        }

        // Refazer a janela de dentro do clique apagaria o elemento que ainda está tratando o evento
        private void Later() => rootVisualElement.schedule.Execute(Rebuild);

        private static VisualElement WrapRow()
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            return wrap;
        }

        private VisualElement Swatch(string hex, string tooltip, bool selected, bool removable)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);

            var swatch = new VisualElement { tooltip = tooltip };
            swatch.style.width = SwatchSize;
            swatch.style.height = SwatchSize;
            swatch.style.backgroundColor = color;
            Margin(swatch, 2f);
            Radius(swatch, 5f);
            Border(swatch, 2f, selected ? TextColor : Color.clear);
            HoverFeedback(swatch);

            swatch.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) SetColor(hex, true);
                else if (e.button == 1 && removable) FolderColorState.RemoveFromPalette(hex);
                else return;
                Later();
            });
            return swatch;
        }

        private Button Mode(string text, string tooltip, bool selected, string color)
        {
            var button = FlatButton(text, false, () =>
            {
                SetColor(color, true);
                Later();
            });
            button.tooltip = tooltip;
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.marginLeft = 2f;
            button.style.marginRight = 2f;
            if (selected) Border(button, 1f, TextColor);
            return button;
        }

        private VisualElement Badge(string key, string tooltip, bool selected)
        {
            var button = new VisualElement { tooltip = tooltip };
            button.style.width = SwatchSize + 4f;
            button.style.height = SwatchSize + 4f;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.backgroundColor = ControlColor;
            Margin(button, 2f);
            Radius(button, 5f);
            Border(button, 1f, selected ? TextColor : Color.clear);
            HoverFeedback(button);

            if (key == "")
            {
                var none = new Label("–");
                none.style.color = MutedColor;
                button.Add(none);
            }
            else
            {
                var icon = new Image { image = FolderColors.BadgeIcon(key) };
                icon.style.width = 16f;
                icon.style.height = 16f;
                button.Add(icon);
            }

            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                SetBadge(key);
                Later();
            });
            return button;
        }
    }
}
