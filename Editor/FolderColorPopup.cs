using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static IsaDo3D.EnvironmentArt.IsaDo3DStyles;

namespace IsaDo3D.EnvironmentArt
{
    // Janela pequena que abre no meio da Unity pelo clique direito na pasta: cores pra clicar, cor livre, selos. O
    // que se escolhe vale na hora e fica, o Undo é que desfaz. Enquanto está aberta ela acompanha a pasta
    // selecionada na janela Project. É janela de verdade (ShowUtility) pra quem move e fecha ser o sistema
    internal class FolderColorPopup : EditorWindow
    {
        private const float Width = 340f;
        private const float SwatchSize = 26f;
        private const float BadgeSize = 30f;

        // Altura de antes do conteúdo ser medido, perto da final pra janela não pular ao abrir
        private const float OpenHeight = 438f;

        private static readonly string[] ModeNames = { "Same as parent", "No color" };
        private static readonly string[] ModeColors = { FolderColorState.Inherit, FolderColorState.NoColor };

        // Sem isto a Unity guarda o campo na recompilação, e a janela voltaria sem o histórico pra desfazer
        [NonSerialized] private string[] _guids;
        private VisualElement _page;
        private VisualElement _body;

        // Como as pastas estavam antes de cada mudança feita aqui, pro Undo. Pode ter pasta que já não é a da janela
        private readonly List<Dictionary<string, (string Color, string Badge)>> _history =
            new List<Dictionary<string, (string Color, string Badge)>>();
        private Button _undo;
        private bool _picking;

        internal static void Open(string[] guids)
        {
            // Uma por vez
            foreach (var open in Resources.FindObjectsOfTypeAll<FolderColorPopup>()) open.Close();

            var popup = CreateInstance<FolderColorPopup>();
            popup._guids = guids;
            popup.titleContent = new GUIContent("Folder Color");
            popup.ShowUtility();

            // No meio da Unity, toda vez. Sem isto ela reabre onde foi fechada da última vez
            var main = EditorGUIUtility.GetMainWindowPosition();
            popup.position = new Rect(main.center.x - Width * 0.5f, main.center.y - OpenHeight * 0.5f, Width, OpenHeight);
            popup.Focus();
        }

        private void OnEnable() => Undo.undoRedoPerformed += AfterUndoRedo;

        private void OnDisable() => Undo.undoRedoPerformed -= AfterUndoRedo;

        private void OnDestroy() => FolderColorState.Flush();

        // O Ctrl+Z pode ter mudado a pasta que está na janela
        private void AfterUndoRedo()
        {
            if (_guids != null && _body != null) Later();
        }

        // Umas 10 vezes por segundo. Não dá pra esperar o Selection.selectionChanged: clicar numa pasta da árvore
        // da esquerda (duas colunas) não muda a seleção nem avisa, só faz o Selection.assetGUIDs devolver essa pasta
        private void OnInspectorUpdate() => FollowSelection();

        // Clicar em outra pasta com a janela aberta passa a editar essa. Seleção sem pasta nenhuma não muda nada
        private void FollowSelection()
        {
            if (_guids == null || _body == null) return;

            var folders = FolderColors.SelectedFolders();
            if (folders.Length == 0 || folders.SequenceEqual(_guids)) return;

            FolderColorState.Flush();
            _guids = folders;
            _picking = false;
            Later();
        }

        // As peças de visual (cabeçalho, cartão, botão, cores) ficam no IsaDo3DStyles, iguais pra todas as janelas
        private void CreateGUI()
        {
            // Depois de recompilar a janela volta sem saber de que pastas era
            if (_guids == null || _guids.Length == 0)
            {
                EditorApplication.delayCall += Close;
                return;
            }

            var root = rootVisualElement;
            _page = Page(root);
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) Close();
            }, TrickleDown.TrickleDown);

            // Tudo dentro de um elemento só, é a altura dele que a janela copia
            _body = new VisualElement();
            _body.RegisterCallback<GeometryChangedEvent>(_ => FitToContent());
            _page.Add(_body);

            Rebuild();
            root.Focus();
        }

        // A janela tem o tamanho do conteúdo. Só pelo minSize e maxSize: escrever no position daqui joga a
        // janela pro canto da tela, igual na janela de Settings
        private void FitToContent()
        {
            float height = Mathf.Ceil(_body.layout.height + _page.resolvedStyle.paddingTop + _page.resolvedStyle.paddingBottom);
            if (float.IsNaN(height) || height < 1f) return;

            var size = new Vector2(Width, height);
            if (minSize == size && maxSize == size) return;

            if (height > maxSize.y) maxSize = size;
            minSize = size;
            maxSize = size;
        }

        private void Rebuild()
        {
            _body.Clear();

            var own = FolderColorState.Find(_guids[0]);
            string color = own != null ? own.color : FolderColorState.Inherit;
            string badge = own != null ? own.badge : "";

            _body.Add(Header("Folder Color"));
            _body.Add(BuildTopRow());
            _body.Add(BuildColorCard(color));
            _body.Add(BuildBadgeCard(badge));

            var hint = Note("Changes are saved right away, Ctrl+Z takes them back. Click another folder to edit it.");
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.marginTop = 2f;
            _body.Add(hint);
        }

        private VisualElement BuildTopRow()
        {
            var row = Row();
            row.style.marginBottom = 8f;

            var paths = _guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var target = Note(paths.Length == 1 ? Path.GetFileName(paths[0]) : $"{paths.Length} folders");
            target.tooltip = string.Join("\n", paths);
            target.style.fontSize = 12f;
            target.style.color = TextColor;
            target.style.unityFontStyleAndWeight = FontStyle.Bold;
            target.style.flexGrow = 1f;
            target.style.flexShrink = 1f;
            target.style.whiteSpace = WhiteSpace.NoWrap;
            target.style.overflow = Overflow.Hidden;
            row.Add(target);

            _undo = LinkButton("Undo", UndoLast);
            _undo.tooltip = "Takes back the last change made in this window, even on a folder edited before this one. Ctrl+Z works too.";
            _undo.SetEnabled(_history.Count > 0);
            row.Add(_undo);
            return row;
        }

        private VisualElement BuildColorCard(string color)
        {
            int mode = Array.IndexOf(ModeColors, color);
            string preset = FolderColors.Presets.FirstOrDefault(p => p.Hex == color).Name;
            var card = Card("Color", mode >= 0 ? ModeNames[mode] : preset ?? color, out _);

            var swatches = WrapRow();
            foreach (var option in FolderColors.Presets)
                swatches.Add(Swatch(option.Hex, option.Name, option.Hex == color, false));
            foreach (var saved in FolderColorState.Palette)
                swatches.Add(Swatch(saved, $"{saved} (right-click to remove)", saved == color, true));
            card.Add(swatches);

            var customTitle = Section("Custom");
            customTitle.style.marginTop = 10f;
            card.Add(customTitle);

            var custom = Row();
            var picker = new ColorField { showAlpha = false, hdr = false, tooltip = "Any color. Shows on the folder while you pick." };
            picker.style.flexGrow = 1f;
            picker.style.marginLeft = 0f;
            picker.style.marginRight = 8f;
            picker.SetValueWithoutNotify(ColorUtility.TryParseHtmlString(color, out var parsed) ? parsed : Color.white);
            picker.RegisterValueChangedCallback(e => SetColor(ToHex(e.newValue), false));
            custom.Add(picker);
            var save = FlatButton("Save swatch", false, () =>
            {
                FolderColorState.AddToPalette(ToHex(picker.value));
                Later();
            });
            save.tooltip = "Keeps this color in the grid above.";
            Padding(save, 0f, 10f);
            custom.Add(save);
            card.Add(custom);

            var modes = new SegmentedBar(ModeNames, index =>
            {
                SetColor(ModeColors[index], true);
                Later();
            });
            modes.tooltip = "Same as parent follows the folder above, and is the default. No color stays plain even when the folder above has a color.";
            modes.style.marginTop = 10f;
            modes.Select(mode);
            card.Add(modes);

            if (color == FolderColorState.Inherit)
            {
                var source = Note(InheritedFrom(AssetDatabase.GUIDToAssetPath(_guids[0])));
                source.style.marginTop = 8f;
                card.Add(source);
            }

            var inside = FolderColorState.ColoredInside(_guids);
            if (inside.Count > 0) card.Add(BuildResetRow(inside));
            return card;
        }

        // De onde vem a cor de uma pasta que está em Same as parent
        private static string InheritedFrom(string path)
        {
            int slash = path.LastIndexOf('/');
            var deciding = slash < 0 ? null : FolderColors.Deciding(path.Substring(0, slash), out path);
            if (deciding == null) return "No folder above has a color, so this one stays plain.";

            string folder = Path.GetFileName(path);
            return deciding.color == FolderColorState.NoColor
                ? $"Plain, because {folder} is set to No color."
                : $"Uses {FolderColors.ColorName(deciding.color)}, from {folder}.";
        }

        // Só aparece quando tem pasta de dentro com cor própria
        private VisualElement BuildResetRow(List<string> inside)
        {
            var row = Row();
            row.style.marginTop = 10f;

            var count = Note(inside.Count == 1 ? "1 folder inside has its own color." : $"{inside.Count} folders inside have their own color.");
            count.tooltip = string.Join("\n", inside.Select(AssetDatabase.GUIDToAssetPath));
            count.style.flexGrow = 1f;
            count.style.flexShrink = 1f;
            count.style.marginRight = 8f;
            row.Add(count);

            var reset = FlatButton("Reset subfolders", false, () =>
            {
                Remember(inside);
                _picking = false;
                FolderColorState.Edit(inside, entry => entry.color = FolderColorState.Inherit);
                Later();
            });
            reset.tooltip = "Sets every folder inside back to Same as parent. Badges stay.";
            Padding(reset, 0f, 10f);
            row.Add(reset);
            return row;
        }

        private VisualElement BuildBadgeCard(string badge)
        {
            string name = FolderColors.Badges.FirstOrDefault(b => b.Key == badge).Name;
            var card = Card("Badge", name ?? "None", out _);

            var badges = WrapRow();
            badges.Add(Badge("", "No badge", badge == ""));
            foreach (var option in FolderColors.Badges)
                badges.Add(Badge(option.Key, option.Name, badge == option.Key));
            card.Add(badges);
            return card;
        }

        private static string ToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        // Sem save é o seletor de cor arrastando, e o arrasto inteiro conta como uma mudança só
        private void SetColor(string color, bool save)
        {
            if (save || !_picking) Remember(_guids);
            _picking = !save;
            FolderColorState.Edit(_guids, entry => entry.color = color, save);
        }

        private void SetBadge(string badge)
        {
            Remember(_guids);
            _picking = false;
            FolderColorState.Edit(_guids, entry => entry.badge = badge);
        }

        private void Remember(IEnumerable<string> guids)
        {
            var before = new Dictionary<string, (string Color, string Badge)>();
            foreach (var guid in guids)
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

            Restore(before);
            Later();
        }

        private void Restore(Dictionary<string, (string Color, string Badge)> before)
        {
            FolderColorState.Edit(before.Keys, entry =>
            {
                entry.color = before[entry.guid].Color;
                entry.badge = before[entry.guid].Badge;
            });
        }

        // Refazer a janela de dentro do clique apagaria o elemento que ainda está tratando o evento
        private void Later() => rootVisualElement.schedule.Execute(Rebuild);

        // As margens dos itens sobram pra fora, pra primeira e a última coluna encostarem na borda do cartão
        private static VisualElement WrapRow()
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexWrap = Wrap.Wrap;
            wrap.style.marginLeft = -3f;
            wrap.style.marginRight = -3f;
            return wrap;
        }

        private VisualElement Swatch(string hex, string tooltip, bool selected, bool removable)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);

            var swatch = new VisualElement { tooltip = tooltip };
            swatch.style.width = SwatchSize;
            swatch.style.height = SwatchSize;
            swatch.style.backgroundColor = color;
            Margin(swatch, 3f);
            Radius(swatch, 6f);
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

        // Selo escolhido fica com a cor de destaque, como a aba ativa das outras janelas
        private VisualElement Badge(string key, string tooltip, bool selected)
        {
            var button = new VisualElement { tooltip = tooltip };
            button.style.width = BadgeSize;
            button.style.height = BadgeSize;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.backgroundColor = selected ? Accent : ControlColor;
            Margin(button, 3f);
            Radius(button, 6f);
            HoverFeedback(button);

            if (key == "")
            {
                var none = new Label("–");
                none.pickingMode = PickingMode.Ignore;
                none.style.color = selected ? Color.white : MutedColor;
                button.Add(none);
            }
            else
            {
                var icon = new Image { image = FolderColors.BadgeIcon(key), pickingMode = PickingMode.Ignore };
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
