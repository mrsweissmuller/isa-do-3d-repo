using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static IsaDo3D.EnvironmentArt.IsaDo3DStyles;

namespace IsaDo3D.EnvironmentArt
{
    // A janela. As ações ficam no outro arquivo desta classe
    public partial class HierarchyOrganizerTool
    {
        private static readonly List<string> AlignLabels = new List<string> { "Left", "Center", "Right" };
        private static readonly TextAnchor[] AlignValues =
        {
            TextAnchor.MiddleLeft, TextAnchor.MiddleCenter, TextAnchor.MiddleRight,
        };

        private static readonly List<string> FontLabels = new List<string> { "Normal", "Bold", "Italic", "Bold italic" };
        private static readonly FontStyle[] FontValues =
        {
            FontStyle.Normal, FontStyle.Bold, FontStyle.Italic, FontStyle.BoldAndItalic,
        };

        private const int LintTab = 2;
        private static readonly string[] TabNames = { "Organize", "Banner", "Lint" };

        private static readonly Color[] QuickColors =
        {
            Hex(0x3D64C7), Hex(0x4B8F27), Hex(0xBB7723), Hex(0x7137A5), Hex(0xB85B88),
            Hex(0x539BA5), Hex(0xB74843), Hex(0xB1992F), Hex(0x6E6E6E), Hex(0x3F3F46),
        };

        private static readonly System.Text.RegularExpressions.Regex DefaultUnityName =
            new System.Text.RegularExpressions.Regex(
                @"^(GameObject|New Game Object|Cube|Sphere|Capsule|Cylinder|Plane|Quad)( \(\d+\))?$");

        private class LintIssue
        {
            public string Message;
            public GameObject[] Objects;
            public string FixLabel;
            public System.Action Fix;
        }

        [SerializeField] private int _tab;

        // Estilo fica num arquivo e não passa pelo Ctrl+Z da Unity, então a aba guarda os próprios passos
        private class UndoStep
        {
            public string Label;
            public double Time;
            public Dictionary<string, BannerStyle> Styles;
            public GameObject Renamed;
            public string OldName;
        }

        private const int MaxUndoSteps = 50;
        private readonly List<UndoStep> _undoSteps = new List<UndoStep>();
        private Button _undoButton;
        private VisualElement _actionsRow;

        private VisualElement[] _pages;
        private VisualElement[] _tabButtons;
        private Label[] _tabLabels;
        private Label _lintBadge;

        private Label _selectedCount;
        private VisualElement _groupButtons;
        private Toggle _dryRunToggle;
        private Button _normalizeButton;
        private TextField _separatorName;

        private Label _editingPill;
        private Label _bannerHint;
        private VisualElement _styleFields;
        private TextField _titleField;
        private Toggle _backgroundToggle;
        private ColorField _backgroundColor;
        private ColorField _textColor;
        private Button _autoTextButton;
        private DropdownField _alignField;
        private DropdownField _fontField;
        private Button _resetButton;
        private Button _convertButton;

        private Label _previewBanner;
        private VisualElement _previewChild;
        private Label _previewHint;
        private VisualElement _quickColors;

        private Toggle _tintToggle;
        private Slider _tintSlider;
        private Toggle _linesToggle;

        private Label _lintScene;
        private VisualElement _lintList;

        private void OnEnable()
        {
            HierarchyGroupColors.Changed += Refresh;
            HierarchyViewPrefs.Changed += Refresh;
        }

        private void OnDisable()
        {
            HierarchyGroupColors.Changed -= Refresh;
            HierarchyViewPrefs.Changed -= Refresh;
        }

        private void OnSelectionChange()
        {
            Refresh();
        }

        private void OnHierarchyChange()
        {
            Refresh();
        }

        // ---------- Montagem ----------

        // As peças de visual (cartão, botão, cores) ficam no IsaDo3DStyles, iguais pra todas as janelas
        private void CreateGUI()
        {
            minSize = new Vector2(340f, 320f);

            var page = Page(rootVisualElement);

            page.Add(Header("Hierarchy Organizer"));
            page.Add(BuildTabs());

            _pages = new[] { BuildOrganizePage(), BuildBannerPage(), BuildLintPage() };
            foreach (var tabPage in _pages) page.Add(tabPage);

            SelectTab(Mathf.Clamp(_tab, 0, _pages.Length - 1));
        }

        private VisualElement BuildTabs()
        {
            var bar = Row();
            bar.style.backgroundColor = CardColor;
            bar.style.marginBottom = 10f;
            Radius(bar, 7f);
            Padding(bar, 3f, 3f);

            _tabButtons = new VisualElement[TabNames.Length];
            _tabLabels = new Label[TabNames.Length];

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;

                var tab = Row();
                tab.style.flexGrow = 1f;
                tab.style.flexBasis = 0f;
                tab.style.height = 24f;
                tab.style.justifyContent = Justify.Center;
                Radius(tab, 5f);
                tab.AddManipulator(new Clickable(() => SelectTab(index)));

                var label = new Label(TabNames[i]);
                label.pickingMode = PickingMode.Ignore;
                tab.Add(label);

                if (i == LintTab)
                {
                    _lintBadge = new Label();
                    _lintBadge.pickingMode = PickingMode.Ignore;
                    _lintBadge.style.fontSize = 10f;
                    _lintBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
                    _lintBadge.style.color = Color.white;
                    _lintBadge.style.backgroundColor = Danger;
                    _lintBadge.style.marginLeft = 6f;
                    Padding(_lintBadge, 0f, 6f);
                    Radius(_lintBadge, 8f);
                    tab.Add(_lintBadge);
                }

                _tabButtons[i] = tab;
                _tabLabels[i] = label;
                bar.Add(tab);
            }

            return bar;
        }

        private void SelectTab(int index)
        {
            _tab = index;

            for (int i = 0; i < _pages.Length; i++)
            {
                bool active = i == index;
                _pages[i].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                _tabButtons[i].style.backgroundColor = active ? Accent : Color.clear;
                _tabLabels[i].style.color = active ? Color.white : MutedColor;
                _tabLabels[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }

            Refresh();
        }

        private VisualElement BuildOrganizePage()
        {
            var page = new VisualElement();

            var defaults = Card("Default groups", "", out _);
            var create = FlatButton("Create default groups", true, CreateDefaultGroups);
            create.tooltip = "Adds the missing default groups to the scene root and puts them in order.";
            defaults.Add(create);
            page.Add(defaults);

            var move = Card("Move selection to group", "", out _selectedCount);
            _groupButtons = new VisualElement();
            move.Add(_groupButtons);
            page.Add(move);

            var separators = Card("Separators", "", out _);
            var separatorRow = Row();
            _separatorName = new TextField { value = "SECTION", tooltip = "Name shown between the lines." };
            _separatorName.style.flexGrow = 1f;
            _separatorName.style.marginLeft = 0f;
            separatorRow.Add(_separatorName);

            var addSeparator = FlatButton("Add separator", true, AddSeparator);
            addSeparator.tooltip = "Adds a separator above the selected root object, or at the end of the scene.";
            addSeparator.style.height = 22f;
            addSeparator.style.marginLeft = 4f;
            separatorRow.Add(addSeparator);
            separators.Add(separatorRow);
            page.Add(separators);

            var auto = Card("Auto organize", "", out _);
            _dryRunToggle = new Toggle("Dry run") { tooltip = "Logs where each object would go, without moving anything." };
            _dryRunToggle.RegisterValueChangedCallback(e => _simulateOnly = e.newValue);
            _dryRunToggle.style.marginBottom = 6f;
            auto.Add(_dryRunToggle);

            var organize = FlatButton("Organize loose root objects", false, AutoOrganize);
            organize.tooltip = "Moves each loose root object to the group that fits it. Objects with no rule stay put.";
            auto.Add(organize);
            page.Add(auto);

            var names = Card("Names", "", out _);
            _normalizeButton = FlatButton("Normalize selected names", false, NormalizeSelectedNames);
            _normalizeButton.tooltip = "Removes Unity's duplicate suffixes, like (1), and renumbers the selected objects.";
            names.Add(_normalizeButton);
            page.Add(names);

            return page;
        }

        private VisualElement BuildBannerPage()
        {
            // Com a janela estreita a coluna da direita desce
            var page = new VisualElement();
            page.style.flexDirection = FlexDirection.Row;
            page.style.flexWrap = Wrap.Wrap;
            page.style.alignItems = Align.FlexStart;

            var left = new VisualElement();
            left.style.flexGrow = 1f;
            left.style.flexBasis = 320f;
            left.style.minWidth = 280f;
            left.style.marginRight = 8f;

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.flexBasis = 280f;
            right.style.minWidth = 260f;

            left.Add(BuildBannerCard());
            right.Add(BuildPreviewCard());
            right.Add(BuildQuickColorsCard());
            right.Add(BuildViewCard());

            page.Add(left);
            page.Add(right);
            return page;
        }

        private VisualElement BuildBannerCard()
        {
            var card = Card("Banner", "", out _editingPill);
            _editingPill.style.color = Dark ? Hex(0xC9D8FF) : Hex(0x1F3F99);
            _editingPill.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.28f);
            Padding(_editingPill, 1f, 8f);
            Radius(_editingPill, 8f);

            _bannerHint = Note("");
            _bannerHint.style.marginBottom = 8f;
            card.Add(_bannerHint);

            _convertButton = FlatButton("Make it a banner", true, () =>
            {
                var target = Target();
                if (target != null && !IsBanner(target))
                    RenameBanner(target, null, CleanName(target.name));
            });
            card.Add(_convertButton);

            _titleField = new TextField("Title") { isDelayed = true };
            _titleField.RegisterValueChangedCallback(e => RenameTarget(e.newValue));
            card.Add(_titleField);

            _styleFields = new VisualElement();
            card.Add(_styleFields);

            _backgroundToggle = new Toggle("Background");
            _backgroundToggle.RegisterValueChangedCallback(e => EditSelected("background", s => s.NoBackground = !e.newValue));
            _styleFields.Add(_backgroundToggle);

            _backgroundColor = new ColorField("    Color") { showAlpha = false };
            _backgroundColor.RegisterValueChangedCallback(e => EditSelected("background color", s => s.Background = e.newValue));
            _styleFields.Add(_backgroundColor);

            var textRow = Row();
            _textColor = new ColorField("Text color") { showAlpha = false };
            _textColor.style.flexGrow = 1f;
            _textColor.RegisterValueChangedCallback(e => EditSelected("text color", s => s.Text = e.newValue));
            textRow.Add(_textColor);

            _autoTextButton = FlatButton("Auto", false, () => EditSelected("auto text", s => s.Text = null));
            _autoTextButton.tooltip = "Picks the text color from the background.";
            _autoTextButton.style.width = 48f;
            _autoTextButton.style.marginLeft = 4f;
            textRow.Add(_autoTextButton);
            _styleFields.Add(textRow);

            var layoutRow = Row();
            _alignField = new DropdownField("Layout", AlignLabels, 0);
            _alignField.style.flexGrow = 1f;
            _alignField.RegisterValueChangedCallback(e =>
            {
                int index = AlignLabels.IndexOf(e.newValue);
                if (index >= 0) EditSelected("align", s => s.Align = AlignValues[index]);
            });
            layoutRow.Add(_alignField);

            _fontField = new DropdownField(FontLabels, 0);
            _fontField.style.width = 110f;
            _fontField.RegisterValueChangedCallback(e =>
            {
                int index = FontLabels.IndexOf(e.newValue);
                if (index >= 0) EditSelected("font", s => s.Font = FontValues[index]);
            });
            layoutRow.Add(_fontField);
            _styleFields.Add(layoutRow);

            // Fora dos campos de estilo, o separador não tem estilo mas tem o Undo do rename
            var actions = Row();
            actions.style.marginTop = 10f;
            _actionsRow = actions;

            _undoButton = FlatButton("Undo", false, UndoLast);
            _undoButton.tooltip = "Undoes the last change made in this tab.";
            _undoButton.style.flexGrow = 1f;
            _undoButton.style.flexBasis = 0f;
            _undoButton.style.marginRight = 4f;
            actions.Add(_undoButton);

            _resetButton = FlatButton("Reset to default", false, () =>
            {
                PushUndo("reset");
                foreach (var name in SelectedBannerNames())
                    HierarchyGroupColors.Clear(name);
            });
            _resetButton.tooltip = "Styles are saved by banner name and shared with the team.";
            _resetButton.style.flexGrow = 1f;
            _resetButton.style.flexBasis = 0f;
            actions.Add(_resetButton);
            card.Add(actions);

            return card;
        }

        private VisualElement BuildPreviewCard()
        {
            var card = Card("Preview", "as in the Hierarchy", out _);

            var box = new VisualElement();
            box.style.backgroundColor = InsetColor;
            Radius(box, 5f);
            Padding(box, 4f, 6f);

            var parent = new Label("▼  Parent");
            parent.style.height = 20f;
            parent.style.unityTextAlign = TextAnchor.MiddleLeft;
            parent.style.color = TextColor;
            box.Add(parent);

            _previewBanner = new Label();
            _previewBanner.style.height = 20f;
            _previewBanner.style.marginLeft = 22f;
            Padding(_previewBanner, 0f, 6f);
            box.Add(_previewBanner);

            _previewChild = new VisualElement();
            _previewChild.style.height = 20f;
            _previewChild.style.marginLeft = 22f;
            var model = new Label("Model");
            model.style.height = 20f;
            model.style.paddingLeft = 16f;
            model.style.unityTextAlign = TextAnchor.MiddleLeft;
            model.style.color = TextColor;
            _previewChild.Add(model);
            box.Add(_previewChild);

            _previewHint = Note("Select a banner");
            _previewHint.style.marginLeft = 22f;
            _previewHint.style.height = 40f;
            _previewHint.style.unityTextAlign = TextAnchor.MiddleLeft;
            box.Add(_previewHint);

            card.Add(box);
            return card;
        }

        private VisualElement BuildQuickColorsCard()
        {
            var card = Card("Quick recolor", "click to paint the selection", out _);

            _quickColors = new VisualElement();
            const int columns = 5;
            for (int start = 0; start < QuickColors.Length; start += columns)
            {
                var row = Row();
                for (int i = start; i < start + columns && i < QuickColors.Length; i++)
                {
                    var color = QuickColors[i];
                    var swatch = new Button(() => EditSelected("recolor", s =>
                    {
                        s.NoBackground = false;
                        s.Background = color;
                    }));
                    swatch.style.backgroundColor = color;
                    swatch.style.flexGrow = 1f;
                    swatch.style.flexBasis = 0f;
                    swatch.style.height = 22f;
                    Border(swatch, 0f, Color.clear);
                    Radius(swatch, 4f);
                    Margin(swatch, 2f);
                    row.Add(swatch);
                }
                _quickColors.Add(row);
            }

            card.Add(_quickColors);
            return card;
        }

        // Gosto de cada um, não entra no arquivo compartilhado
        private VisualElement BuildViewCard()
        {
            var card = Card("Objects inside groups", "only you", out _);

            _tintToggle = new Toggle("Tint rows");
            _tintToggle.RegisterValueChangedCallback(e => HierarchyViewPrefs.ChildTint = e.newValue);
            card.Add(_tintToggle);

            _tintSlider = new Slider("    Strength", HierarchyViewPrefs.MinTintAlpha, HierarchyViewPrefs.MaxTintAlpha);
            _tintSlider.RegisterValueChangedCallback(e => HierarchyViewPrefs.ChildTintAlpha = e.newValue);
            card.Add(_tintSlider);

            _linesToggle = new Toggle("Connector lines");
            _linesToggle.RegisterValueChangedCallback(e => HierarchyViewPrefs.TreeLines = e.newValue);
            card.Add(_linesToggle);

            return card;
        }

        private VisualElement BuildLintPage()
        {
            var page = new VisualElement();

            var card = Card("Lint", "", out _lintScene);
            _lintList = new VisualElement();
            card.Add(_lintList);
            page.Add(card);

            return page;
        }

        // ---------- Seleção ----------

        private static GameObject Target()
        {
            var target = Selection.activeGameObject;
            return target != null && target.scene.IsValid() ? target : null;
        }

        private static bool IsBanner(GameObject go)
        {
            return go != null && go.scene.IsValid() && go.name.StartsWith(HeaderPrefix);
        }

        // Um por nome, dois grupos com o mesmo nome dividem o estilo
        private static List<string> SelectedBannerNames()
        {
            return Selection.gameObjects
                .Where(IsBanner)
                .Select(go => CleanName(go.name))
                .GroupBy(CanonicalGroupKey)
                .Select(group => group.First())
                .ToList();
        }

        private void EditSelected(string label, System.Action<BannerStyle> change)
        {
            var names = SelectedBannerNames();
            if (names.Count == 0) return;

            PushUndo(label);
            foreach (var name in names)
                HierarchyGroupColors.Edit(name, change);
        }

        private void PushUndo(string label, GameObject renamed = null, string oldName = null)
        {
            double now = EditorApplication.timeSinceStartup;

            // Arrastar no seletor de cor manda dezenas de mudanças, e elas contam como um passo só
            if (renamed == null && _undoSteps.Count > 0)
            {
                var last = _undoSteps[_undoSteps.Count - 1];
                if (last.Renamed == null && last.Label == label && now - last.Time < 0.6)
                {
                    last.Time = now;
                    return;
                }
            }

            _undoSteps.Add(new UndoStep
            {
                Label = label,
                Time = now,
                Styles = HierarchyGroupColors.Snapshot(),
                Renamed = renamed,
                OldName = oldName,
            });
            if (_undoSteps.Count > MaxUndoSteps) _undoSteps.RemoveAt(0);
        }

        private void UndoLast()
        {
            if (_undoSteps.Count == 0) return;

            var step = _undoSteps[_undoSteps.Count - 1];
            _undoSteps.RemoveAt(_undoSteps.Count - 1);

            if (step.Renamed != null)
            {
                Undo.RecordObject(step.Renamed, "Rename banner");
                step.Renamed.name = step.OldName;
                EditorSceneManager.MarkSceneDirty(step.Renamed.scene);
                EditorApplication.DirtyHierarchyWindowSorting();
            }

            HierarchyGroupColors.Restore(step.Styles);
            Refresh();
        }

        private void RenameTarget(string typed)
        {
            var target = Target();
            if (target == null) return;

            bool isTitle = IsTitle(target.name);
            if (!isTitle && !IsBanner(target)) return;

            string oldClean = isTitle ? CleanTitle(target.name) : CleanName(target.name);
            string newClean = isTitle ? CleanTitle(typed ?? "") : CleanName(typed ?? "");
            if (newClean.Length == 0 || newClean == oldClean)
            {
                Refresh();
                return;
            }

            if (isTitle) RenameSeparator(target, newClean);
            else RenameBanner(target, oldClean, newClean);
        }

        private void RenameSeparator(GameObject target, string newCleanTitle)
        {
            PushUndo("rename", target, target.name);
            Undo.RecordObject(target, "Rename separator");
            target.name = TitleName(newCleanTitle);
            EditorSceneManager.MarkSceneDirty(target.scene);
            EditorApplication.DirtyHierarchyWindowSorting();
            Refresh();
        }

        // oldCleanName nulo é objeto comum virando banner
        private void RenameBanner(GameObject target, string oldCleanName, string newCleanName)
        {
            if (string.IsNullOrEmpty(newCleanName)) return;

            PushUndo("rename", target, target.name);
            Undo.RecordObject(target, "Rename banner");
            target.name = BannerName(newCleanName);
            EditorSceneManager.MarkSceneDirty(target.scene);

            if (oldCleanName != null) HierarchyGroupColors.CopyIfMissing(oldCleanName, newCleanName);
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.DirtyHierarchyWindowSorting();
            Refresh();
        }

        // Sempre na raiz, é onde ficam os grupos que ele separa
        private void AddSeparator()
        {
            string clean = CleanTitle(_separatorName.value ?? "");
            if (clean.Length == 0) clean = "SECTION";

            var scene = SceneManager.GetActiveScene();
            var above = Target();
            int index = above != null && above.scene == scene ? above.transform.root.GetSiblingIndex() : -1;

            var separator = new GameObject(TitleName(clean));
            Undo.RegisterCreatedObjectUndo(separator, "Add separator");
            if (separator.scene != scene) SceneManager.MoveGameObjectToScene(separator, scene);
            if (index >= 0) separator.transform.SetSiblingIndex(index);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = separator;
        }

        private static string BannerName(string cleanName)
        {
            return $"{HeaderPrefix} {cleanName} {HeaderPrefix}";
        }

        private static string TitleName(string cleanTitle)
        {
            return $"{TitlePrefix} {cleanTitle} {TitlePrefix}";
        }

        // ---------- Atualização ----------

        private void Refresh()
        {
            if (_pages == null) return;

            RefreshOrganize();
            RefreshBanner();
            RefreshSettings();
            RefreshLint();
        }

        private static void Show(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshOrganize()
        {
            int selected = SelectedSceneObjects().Length;
            _selectedCount.text = $"{selected} selected";
            _dryRunToggle.SetValueWithoutNotify(_simulateOnly);
            _normalizeButton.SetEnabled(selected > 0);

            _groupButtons.Clear();
            var groups = FindHeaderGroups();
            if (groups.Count == 0)
            {
                _groupButtons.Add(Note("No groups in this scene yet."));
                return;
            }

            foreach (var group in groups)
            {
                var target = group;
                var look = HierarchyHeaderRenderer.LookFor(group.name);

                var button = new Button(() => MoveSelectionToGroup(target))
                {
                    text = $"{look.CleanName}  ({group.transform.childCount})",
                    tooltip = look.Tooltip,
                };
                button.style.backgroundColor = look.HasBackground ? look.Background : ControlColor;
                button.style.color = look.Text;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.height = 22f;
                button.style.marginBottom = 3f;
                button.style.marginLeft = 0f;
                button.style.marginRight = 0f;
                button.style.marginTop = 0f;
                Border(button, 0f, Color.clear);
                Radius(button, 4f);
                button.SetEnabled(selected > 0);
                _groupButtons.Add(button);
            }
        }

        private void RefreshBanner()
        {
            var target = Target();
            bool isBanner = IsBanner(target);
            bool isTitle = target != null && IsTitle(target.name);

            _editingPill.text = target == null ? "" : $"Editing {target.name}";
            Show(_editingPill, target != null);

            if (target == null) _bannerHint.text = "Select an object in the Hierarchy.";
            else if (!isBanner) _bannerHint.text = "This object is not a banner.";
            Show(_bannerHint, !isBanner && !isTitle);

            Show(_convertButton, target != null && !isBanner && !isTitle);
            Show(_styleFields, isBanner);
            Show(_previewBanner, isBanner);
            Show(_previewChild, isBanner);
            Show(_previewHint, !isBanner);

            bool anyBanner = SelectedBannerNames().Count > 0;
            _quickColors.SetEnabled(anyBanner);

            Show(_titleField, isBanner || isTitle);
            Show(_actionsRow, isBanner || isTitle);
            _undoButton.SetEnabled(_undoSteps.Count > 0);
            _resetButton.SetEnabled(false);
            if (isTitle) _titleField.SetValueWithoutNotify(CleanTitle(target.name));

            if (!isBanner) return;

            string cleanName = CleanName(target.name);
            var style = HierarchyGroupColors.Get(cleanName);
            var look = HierarchyHeaderRenderer.LookFor(target.name);
            var anchor = look.Anchor ?? TextAnchor.MiddleLeft;

            _titleField.SetValueWithoutNotify(cleanName);
            _backgroundToggle.SetValueWithoutNotify(look.HasBackground);
            _backgroundColor.SetValueWithoutNotify(look.Background);
            _backgroundColor.SetEnabled(look.HasBackground);
            _textColor.SetValueWithoutNotify(look.Text);
            _autoTextButton.SetEnabled(style?.Text != null);
            _alignField.SetValueWithoutNotify(AlignLabels[Mathf.Max(0, System.Array.IndexOf(AlignValues, anchor))]);
            _fontField.SetValueWithoutNotify(FontLabels[Mathf.Max(0, System.Array.IndexOf(FontValues, look.Font))]);
            _resetButton.SetEnabled(style != null);
            _undoButton.SetEnabled(_undoSteps.Count > 0);

            _previewBanner.text = look.CleanName;
            _previewBanner.style.backgroundColor = look.HasBackground ? look.Background : Color.clear;
            _previewBanner.style.color = look.Text;
            _previewBanner.style.unityTextAlign = anchor;
            _previewBanner.style.unityFontStyleAndWeight = look.Font;

            var tint = Color.clear;
            if (look.HasBackground && HierarchyViewPrefs.ChildTint)
            {
                tint = look.Background;
                tint.a = HierarchyViewPrefs.ChildTintAlpha;
            }
            _previewChild.style.backgroundColor = tint;
        }

        private void RefreshSettings()
        {
            _tintToggle.SetValueWithoutNotify(HierarchyViewPrefs.ChildTint);
            _tintSlider.SetValueWithoutNotify(HierarchyViewPrefs.ChildTintAlpha);
            _tintSlider.SetEnabled(HierarchyViewPrefs.ChildTint);
            _linesToggle.SetValueWithoutNotify(HierarchyViewPrefs.TreeLines);
        }

        private void RefreshLint()
        {
            var scene = SceneManager.GetActiveScene();
            _lintScene.text = scene.IsValid() ? scene.name : "";

            var issues = FindIssues(scene);
            _lintBadge.text = issues.Count.ToString();
            Show(_lintBadge, issues.Count > 0);

            _lintList.Clear();
            if (issues.Count == 0)
            {
                _lintList.Add(Note("No issues in this scene."));
                return;
            }

            foreach (var issue in issues)
            {
                var current = issue;

                var row = Row();
                row.style.backgroundColor = InsetColor;
                row.style.marginBottom = 4f;
                Padding(row, 5f, 8f);
                Radius(row, 5f);

                var message = new Label(issue.Message);
                message.style.flexGrow = 1f;
                message.style.flexShrink = 1f;
                message.style.whiteSpace = WhiteSpace.Normal;
                message.style.color = TextColor;
                row.Add(message);

                var select = FlatButton("Select", false, () =>
                {
                    Selection.objects = current.Objects.Where(o => o != null).Cast<Object>().ToArray();
                });
                select.style.width = 64f;
                select.style.marginLeft = 6f;
                Show(select, current.Objects.Length > 0);
                row.Add(select);

                if (issue.Fix != null)
                {
                    var fix = FlatButton(issue.FixLabel, true, () =>
                    {
                        current.Fix();
                        Refresh();
                    });
                    fix.style.height = 22f;
                    fix.style.width = 64f;
                    fix.style.marginLeft = 4f;
                    row.Add(fix);
                }

                _lintList.Add(row);
            }
        }

        // ---------- Lint ----------

        // Só a raiz da cena ativa e os filhos diretos dos grupos, varrer a cena inteira a cada mudança ficaria pesado
        private static List<LintIssue> FindIssues(Scene scene)
        {
            var issues = new List<LintIssue>();
            if (!scene.IsValid() || !scene.isLoaded) return issues;

            var roots = scene.GetRootGameObjects();
            var banners = roots.Where(go => go.name.StartsWith(HeaderPrefix)).ToList();
            var titles = roots.Where(go => IsTitle(go.name)).ToList();

            foreach (var banner in banners)
            {
                var go = banner;
                string clean = CleanName(go.name);
                string expected = BannerName(clean);

                if (clean.Length > 0 && go.name != expected)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"\"{go.name}\" should be \"{expected}\".",
                        Objects = new[] { go },
                        FixLabel = "Rename",
                        Fix = () => RenameTo(go, expected),
                    });
                }

                var t = go.transform;
                if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"{clean}: transform is not zeroed.",
                        Objects = new[] { go },
                        FixLabel = "Zero",
                        Fix = () => ZeroKeepingChildren(go),
                    });
                }

                if (go.GetComponents<Component>().Length > 1)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"{clean}: has components besides Transform.",
                        Objects = new[] { go },
                    });
                }
            }

            foreach (var same in banners.GroupBy(go => CanonicalGroupKey(CleanName(go.name))))
            {
                if (same.Count() < 2) continue;

                issues.Add(new LintIssue
                {
                    Message = $"{same.Key}: {same.Count()} groups share this name.",
                    Objects = same.ToArray(),
                });
            }

            foreach (var title in titles)
            {
                var go = title;
                string clean = CleanTitle(go.name);
                string expected = TitleName(clean);

                if (clean.Length > 0 && go.name != expected)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"\"{go.name}\" should be \"{expected}\".",
                        Objects = new[] { go },
                        FixLabel = "Rename",
                        Fix = () => RenameTo(go, expected),
                    });
                }

                if (go.transform.childCount > 0)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"{clean}: separator has objects inside.",
                        Objects = new[] { go },
                    });
                }
            }

            // Cena sem nenhum grupo ainda não usa a organização, então não cobra os padrão
            if (banners.Count > 0)
            {
                var missing = MissingDefaultGroups();
                if (missing.Count > 0)
                {
                    issues.Add(new LintIssue
                    {
                        Message = $"Missing default groups: {string.Join(", ", missing)}.",
                        Objects = new GameObject[0],
                        FixLabel = "Create",
                        Fix = CreateDefaultGroups,
                    });
                }

                var present = PresentDefaultGroups();
                if (!InSiblingOrder(present))
                {
                    issues.Add(new LintIssue
                    {
                        Message = "Default groups are out of order.",
                        Objects = present.ToArray(),
                        FixLabel = "Reorder",
                        Fix = ReorderDefaultGroups,
                    });
                }
            }

            // Raiz e filhos diretos dos grupos, mais fundo que isso pesa a cada mudança na cena
            var defaultNamed = roots
                .Concat(banners.SelectMany(b => b.transform.Cast<Transform>().Select(t => t.gameObject)))
                .Where(go => DefaultUnityName.IsMatch(go.name))
                .ToArray();
            if (defaultNamed.Length > 0)
            {
                issues.Add(new LintIssue
                {
                    Message = $"{defaultNamed.Length} object(s) still have a Unity default name.",
                    Objects = defaultNamed,
                });
            }

            return issues;
        }

        private static void RenameTo(GameObject go, string name)
        {
            if (go == null) return;

            Undo.RecordObject(go, "Rename group");
            go.name = name;
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorApplication.DirtyHierarchyWindowSorting();
        }

        // Os filhos ficam onde estão no mundo
        private static void ZeroKeepingChildren(GameObject group)
        {
            if (group == null) return;

            Undo.SetCurrentGroupName("Zero group transform");
            int undoGroup = Undo.GetCurrentGroup();

            var t = group.transform;
            var children = new List<(Transform Child, Vector3 Position, Quaternion Rotation, Vector3 Scale)>();
            foreach (Transform child in t)
                children.Add((child, child.position, child.rotation, child.lossyScale));

            Undo.RecordObject(t, "Zero group transform");
            foreach (var entry in children)
                Undo.RecordObject(entry.Child, "Zero group transform");

            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            foreach (var entry in children)
            {
                entry.Child.SetPositionAndRotation(entry.Position, entry.Rotation);
                entry.Child.localScale = entry.Scale;
            }

            EditorSceneManager.MarkSceneDirty(group.scene);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
