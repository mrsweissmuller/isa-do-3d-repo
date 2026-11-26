using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static ArtTools.EnvironmentArt.ArtToolsStyles;

namespace ArtTools.EnvironmentArt
{
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

        private class ColorPreset
        {
            public string Name;
            public Color[] Colors;

            public Color ColorAt(int index, int count)
            {
                if (count < 2 || count > Colors.Length) return Colors[index % Colors.Length];
                return Colors[Mathf.RoundToInt(index * (Colors.Length - 1) / (float)(count - 1))];
            }
        }

        private static readonly ColorPreset[] ColorPresets =
        {
            new ColorPreset
            {
                Name = "Grayscale",
                Colors = new[]
                {
                    Hex(0x1C1C1C), Hex(0x303030), Hex(0x474747), Hex(0x5E5E5E),
                    Hex(0x707070), Hex(0xBDBDBD), Hex(0xD6D6D6), Hex(0xEDEDED),
                },
            },
            new ColorPreset
            {
                Name = "Blue",
                Colors = new[]
                {
                    Hex(0x162239), Hex(0x23385C), Hex(0x324D7C), Hex(0x43649F),
                    Hex(0x577EC2), Hex(0xA6C2F4), Hex(0xC2D6F8), Hex(0xDBE7FB),
                },
            },
            new ColorPreset
            {
                Name = "Green",
                Colors = new[]
                {
                    Hex(0x112C17), Hex(0x1B4626), Hex(0x266035), Hex(0x347D46),
                    Hex(0x469B5B), Hex(0x94DAA6), Hex(0xB4E7C1), Hex(0xD2F2DA),
                },
            },
            new ColorPreset
            {
                Name = "Red",
                Colors = new[]
                {
                    Hex(0x3F1413), Hex(0x66211F), Hex(0x8A2F2C), Hex(0xB0403C),
                    Hex(0xD9534E), Hex(0xF9A7A4), Hex(0xFBC3C1), Hex(0xFCDCDB),
                },
            },
            new ColorPreset
            {
                Name = "Orange",
                Colors = new[]
                {
                    Hex(0x381B07), Hex(0x5B2B0B), Hex(0x7B3C11), Hex(0x9F4F19),
                    Hex(0xCB6A29), Hex(0xF6AF80), Hex(0xFAC9A8), Hex(0xFBDFCD),
                },
            },
            new ColorPreset
            {
                Name = "Color blind",
                Colors = new[]
                {
                    Hex(0x3D3069), Hex(0x5F92CD), Hex(0x662E41), Hex(0x7E9745),
                    Hex(0x6E5FD1), Hex(0x9F8374), Hex(0x30662E), Hex(0xAD5186),
                },
            },
            new ColorPreset
            {
                Name = "Synthwave",
                Colors = new[]
                {
                    Hex(0xB0894A), Hex(0xC4704A), Hex(0xD0558C), Hex(0xA84585),
                    Hex(0x7446A8), Hex(0x3F2E6B), Hex(0x4C66BF), Hex(0x4298A6),
                },
            },
            new ColorPreset
            {
                Name = "Rainbow",
                Colors = new[]
                {
                    Hex(0xD93636), Hex(0xDB6F16), Hex(0xF2C91D), Hex(0x3FA34D),
                    Hex(0x1FA3A3), Hex(0x2F6FDE), Hex(0x7A3FD1), Hex(0xD13FA8),
                },
            },
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
        private class AutoRow
        {
            public GameObject Object;
            public VisualElement Row;
            public Toggle Check;
            public DropdownPill Destination;
        }

        private readonly List<AutoRow> _autoRows = new List<AutoRow>();
        private VisualElement _autoBatch;

        private int _autoCheckAnchor = -1;
        private int _autoNameAnchor = -1;
        private Label _autoNote;
        private VisualElement _autoPreview;
        private VisualElement _autoList;
        private Button _autoApply;
        private Button _normalizeButton;
        private TextField _separatorName;
        private TextField _groupName;

        private Label _editingPill;
        private Label _bannerHint;
        private VisualElement _styleFields;
        private TextField _titleField;
        private Toggle _backgroundToggle;
        private ColorField _backgroundColor;
        private ColorField _textColor;
        private Button _autoTextButton;
        private DropdownPill _alignField;
        private DropdownPill _fontField;
        private Button _resetButton;
        private Button _convertButton;

        private Label _previewBanner;
        private VisualElement _previewChild;
        private Label _previewHint;

        private Toggle _tintToggle;
        private Slider _tintSlider;
        private Slider _opacitySlider;
        private Slider _saturationSlider;
        private Slider _lightnessSlider;
        private Button _resetAdjustButton;
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
            RefreshAutoSelection();
        }

        private void OnHierarchyChange()
        {
            Refresh();
        }

        private void CreateGUI()
        {
            minSize = new Vector2(MinWidth, MinHeight);

            _page = Page(rootVisualElement);

            var page = new VisualElement();
            _body = page;
            page.RegisterCallback<GeometryChangedEvent>(FitHeight);
            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(FitHeight);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(FitHeight);
            _page.Add(page);

            page.Add(Header("Hierarchy Organizer"));
            page.Add(BuildTabs());

            _pages = new[] { BuildOrganizePage(), BuildBannerPage(), BuildLintPage() };
            foreach (var tabPage in _pages) page.Add(tabPage);

            SelectTab(Mathf.Clamp(_tab, 0, _pages.Length - 1));
        }

        private const float MinWidth = 340f;
        private const float MinHeight = 320f;
        private const float AnySize = 4000f;

        private const float MaxChrome = 60f;

        private VisualElement _page;
        private VisualElement _body;

        private void FitHeight(GeometryChangedEvent e)
        {
            if (docked)
            {
                if (Mathf.Abs(minSize.y - maxSize.y) < 1f)
                {
                    maxSize = new Vector2(AnySize, AnySize);
                    minSize = new Vector2(MinWidth, MinHeight);
                }
                return;
            }

            float view = rootVisualElement.layout.height;
            float content = _body.layout.height + _page.resolvedStyle.paddingTop + _page.resolvedStyle.paddingBottom;
            if (float.IsNaN(view) || float.IsNaN(content) || view <= 0f || content <= 0f) return;

            content = Mathf.Min(content, EditorGUIUtility.GetMainWindowPosition().height * 0.85f);

            bool locked = Mathf.Abs(minSize.y - maxSize.y) < 1f;
            float height = Mathf.Ceil(locked ? minSize.y + content - view : content);
            height = Mathf.Clamp(height, content, content + MaxChrome);
            if (locked && Mathf.Abs(minSize.y - height) < 1f) return;

            if (height > maxSize.y) maxSize = new Vector2(AnySize, height);
            minSize = new Vector2(MinWidth, height);
            maxSize = new Vector2(AnySize, height);
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
                HoverFeedback(tab);

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

            var defaults = Card("Groups", "", out _);
            var create = FlatButton("Create default groups", true, CreateDefaultGroups);
            create.tooltip = "Adds the missing default groups to the scene root and puts them in order.";
            defaults.Add(create);

            var groupRow = Row();
            groupRow.style.marginTop = 6f;
            _groupName = new TextField { value = "NEW GROUP", tooltip = "Name of the new group." };
            _groupName.style.flexGrow = 1f;
            _groupName.style.marginLeft = 0f;
            groupRow.Add(_groupName);

            var addGroup = FlatButton("New group", false, AddGroup);
            addGroup.tooltip = "Adds a group above the selected root object, or at the end of the scene.";
            addGroup.style.width = 100f;
            addGroup.style.marginLeft = 4f;
            groupRow.Add(addGroup);
            defaults.Add(groupRow);

            var foldRow = Row();
            foldRow.style.marginTop = 6f;

            var expandAll = FlatButton("Expand all", false, () => HierarchyHeaderRenderer.SetExpanded(FindHeaderGroups(), true));
            expandAll.style.flexGrow = 1f;
            expandAll.style.flexBasis = 0f;
            expandAll.style.marginRight = 4f;
            foldRow.Add(expandAll);

            var collapseAll = FlatButton("Collapse all", false, () => HierarchyHeaderRenderer.SetExpanded(FindHeaderGroups(), false));
            collapseAll.style.flexGrow = 1f;
            collapseAll.style.flexBasis = 0f;
            foldRow.Add(collapseAll);
            defaults.Add(foldRow);
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

            var auto = Card("Auto organize", "", out _autoNote);
            var preview = FlatButton("Preview loose root objects", false, BuildAutoPreview);
            preview.tooltip = "Suggests a group for each loose root object. Nothing moves until you confirm.";
            auto.Add(preview);

            _autoPreview = new VisualElement();
            _autoPreview.style.marginTop = 8f;

            _autoBatch = new VisualElement();
            _autoBatch.style.marginBottom = 6f;
            _autoPreview.Add(_autoBatch);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.maxHeight = 260f;
            list.style.backgroundColor = InsetColor;
            Radius(list, 5f);
            _autoList = list.contentContainer;
            Padding(_autoList, 4f, 6f);
            _autoPreview.Add(list);

            var autoActions = Row();
            autoActions.style.marginTop = 8f;

            _autoApply = FlatButton("Move", true, ApplyAutoPreview);
            _autoApply.style.height = 22f;
            _autoApply.style.flexGrow = 1f;
            _autoApply.style.flexBasis = 0f;
            _autoApply.style.marginRight = 4f;
            autoActions.Add(_autoApply);

            var cancel = FlatButton("Cancel", false, ClearAutoPreview);
            cancel.style.width = 70f;
            autoActions.Add(cancel);
            _autoPreview.Add(autoActions);

            auto.Add(_autoPreview);
            Show(_autoPreview, false);
            page.Add(auto);

            var names = Card("Names", "", out _);
            _normalizeButton = FlatButton("Normalize selected names", false, NormalizeSelectedNames);
            _normalizeButton.tooltip = "Removes Unity's duplicate suffixes, like (1), and renumbers the selected objects.";
            names.Add(_normalizeButton);
            page.Add(names);

            return page;
        }

        private void BuildAutoPreview()
        {
            _autoRows.Clear();
            _autoList.Clear();
            _autoBatch.Clear();
            _autoCheckAnchor = -1;
            _autoNameAnchor = -1;

            var plan = PlanAutoOrganize();
            var destinations = OrganizeDestinations();
            Show(_autoPreview, true);

            if (plan.Count == 0)
            {
                _autoList.Add(Note("No loose objects at the root."));
                UpdateAutoCount();
                return;
            }

            BuildAutoBatch(destinations);

            foreach (var (go, suggestion) in plan.OrderBy(p => p.Destination == null))
            {
                var entry = new AutoRow { Object = go };

                var row = Row();
                row.style.height = 22f;
                Radius(row, 3f);
                Padding(row, 0f, 2f);

                var check = new Toggle { value = suggestion != null };
                check.style.marginRight = 4f;

                check.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0 || !e.shiftKey) return;
                    check.SetValueWithoutNotify(!check.value);
                    OnAutoCheck(entry, check.value, true);
                    e.StopPropagation();
                }, TrickleDown.TrickleDown);
                check.RegisterValueChangedCallback(e => OnAutoCheck(entry, e.newValue, false));
                row.Add(check);

                var name = new Label(go.name)
                {
                    tooltip = "Click to select this object. Shift-click selects up to here, Ctrl-click adds or removes it.",
                };
                name.style.flexGrow = 1f;
                name.style.flexShrink = 1f;
                name.style.overflow = Overflow.Hidden;
                name.style.color = suggestion != null ? TextColor : MutedColor;

                name.RegisterCallback<ClickEvent>(e => OnAutoNameClick(entry, e));
                row.Add(name);

                int index = suggestion != null ? Mathf.Max(0, destinations.IndexOf(suggestion)) : 0;
                var destination = new DropdownPill(destinations, index)
                {
                    MenuLabel = DestinationMenuLabel,
                    FieldLabel = DestinationFieldLabel,

                    AlwaysNotify = true,
                };
                destination.style.width = 170f;
                destination.style.flexShrink = 0f;

                destination.Changed += value => OnAutoDestination(entry, value);
                row.Add(destination);

                entry.Row = row;
                entry.Check = check;
                entry.Destination = destination;
                _autoRows.Add(entry);
                _autoList.Add(row);
            }

            RefreshAutoSelection();
            UpdateAutoCount();
        }

        private void BuildAutoBatch(List<string> destinations)
        {
            var hint = Note("Shift-click a checkbox to mark a range. The range turns blue, and a group picked on one blue row goes to all of them.");
            hint.style.marginBottom = 4f;
            _autoBatch.Add(hint);

            var row = Row();
            row.Add(LinkButton("Mark all", () => MarkAllAuto(true)));
            row.Add(LinkButton("Mark none", () => MarkAllAuto(false)));

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            row.Add(spacer);

            var setMarked = new DropdownPill(destinations, 0)
            {
                MenuLabel = DestinationMenuLabel,
                FieldLabel = _ => "Set marked to…",
                AlwaysNotify = true,
                tooltip = "Gives every marked row the group you pick here.",
            };
            setMarked.style.width = 170f;
            setMarked.style.flexShrink = 0f;
            setMarked.Changed += value =>
            {
                foreach (var marked in _autoRows.Where(r => r.Check.value))
                    marked.Destination.SetValueWithoutNotify(value);
            };
            row.Add(setMarked);
            _autoBatch.Add(row);
        }

        private void MarkAllAuto(bool value)
        {
            foreach (var row in _autoRows) row.Check.SetValueWithoutNotify(value);
            if (!value) DeselectAuto(_autoRows);
            UpdateAutoCount();
        }

        private static void DeselectAuto(List<AutoRow> rows)
        {
            var leaving = new HashSet<Object>(rows.Where(r => r.Object != null).Select(r => (Object)r.Object));
            var staying = Selection.objects.Where(o => !leaving.Contains(o)).ToArray();
            if (staying.Length != Selection.objects.Length) Selection.objects = staying;
        }

        private static bool IsAutoSelected(AutoRow row) => row.Object != null && Selection.Contains(row.Object);

        private List<AutoRow> AutoSelectedWith(AutoRow row)
        {
            return IsAutoSelected(row)
                ? _autoRows.Where(r => r != row && IsAutoSelected(r)).ToList()
                : new List<AutoRow>();
        }

        private void RefreshAutoSelection()
        {
            var tint = new Color(Accent.r, Accent.g, Accent.b, 0.3f);
            foreach (var row in _autoRows)
                row.Row.style.backgroundColor = IsAutoSelected(row) ? tint : Color.clear;
        }

        private void OnAutoNameClick(AutoRow row, ClickEvent e)
        {
            if (row.Object == null || e.button != 0) return;

            int index = _autoRows.IndexOf(row);
            bool shift = e.shiftKey;
            bool toggle = e.actionKey;

            if (shift && _autoNameAnchor >= 0 && _autoNameAnchor < _autoRows.Count)
            {
                int from = Mathf.Min(_autoNameAnchor, index);
                int to = Mathf.Max(_autoNameAnchor, index);
                Selection.objects = _autoRows.Skip(from).Take(to - from + 1)
                    .Where(r => r.Object != null).Select(r => (Object)r.Object).ToArray();
                return;
            }

            if (toggle)
            {
                var selected = Selection.objects.ToList();
                if (!selected.Remove(row.Object)) selected.Add(row.Object);
                Selection.objects = selected.ToArray();
            }
            else
            {
                Selection.activeGameObject = row.Object;
                EditorGUIUtility.PingObject(row.Object);
            }
            _autoNameAnchor = index;
        }

        private void OnAutoCheck(AutoRow row, bool value, bool shift)
        {
            int index = _autoRows.IndexOf(row);

            if (shift && _autoCheckAnchor >= 0 && _autoCheckAnchor < _autoRows.Count)
            {
                int from = Mathf.Min(_autoCheckAnchor, index);
                int to = Mathf.Max(_autoCheckAnchor, index);
                for (int i = from; i <= to; i++) _autoRows[i].Check.SetValueWithoutNotify(value);

                var range = _autoRows.Skip(from).Take(to - from + 1).ToList();
                if (value)
                    Selection.objects = range.Where(r => r.Object != null).Select(r => (Object)r.Object).ToArray();
                else
                    DeselectAuto(range);
            }
            else
            {
                var others = AutoSelectedWith(row);
                foreach (var other in others) other.Check.SetValueWithoutNotify(value);

                if (!value && others.Count > 0)
                {
                    others.Add(row);
                    DeselectAuto(others);
                }
            }

            _autoCheckAnchor = index;
            UpdateAutoCount();
        }

        private void OnAutoDestination(AutoRow row, string value)
        {
            foreach (var other in AutoSelectedWith(row))
            {
                other.Destination.SetValueWithoutNotify(value);
                other.Check.SetValueWithoutNotify(true);
            }

            row.Check.SetValueWithoutNotify(true);
            UpdateAutoCount();
        }

        private static string DestinationMenuLabel(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : "      \u21B3 " + path.Substring(slash + 1);
        }

        private static string DestinationFieldLabel(string path)
        {
            return path.Replace("/", " \u203A ");
        }

        private void UpdateAutoCount()
        {
            int total = _autoRows.Count;
            int marked = _autoRows.Count(r => r.Check.value);
            _autoNote.text = total == 0 ? "" : $"{marked} of {total} marked";
            _autoApply.text = $"Move {marked} object(s)";
            _autoApply.SetEnabled(marked > 0);
        }

        private void ApplyAutoPreview()
        {
            var moves = _autoRows
                .Where(r => r.Check.value)
                .Select(r => (r.Object, r.Destination.value))
                .ToList();

            ApplyAutoOrganize(moves);
            ClearAutoPreview();
        }

        private void ClearAutoPreview()
        {
            _autoRows.Clear();
            _autoList.Clear();
            _autoNote.text = "";
            Show(_autoPreview, false);
        }

        private VisualElement BuildBannerPage()
        {
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
            right.Add(BuildPresetsCard());
            right.Add(BuildAdjustCard());
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
            layoutRow.style.marginTop = 2f;
            layoutRow.style.marginLeft = 3f;
            layoutRow.style.marginRight = 3f;

            var layoutLabel = new Label("Layout");
            layoutLabel.style.color = TextColor;
            layoutLabel.style.minWidth = 120f;
            _titleField.labelElement.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 0f) layoutLabel.style.width = e.newRect.width + 2f;
            });
            layoutRow.Add(layoutLabel);

            _alignField = new DropdownPill(AlignLabels, 0);
            _alignField.style.flexGrow = 1f;
            _alignField.style.flexBasis = 0f;
            _alignField.style.marginRight = 4f;
            _alignField.Changed += picked =>
            {
                int index = AlignLabels.IndexOf(picked);
                if (index >= 0) EditSelected("align", s => s.Align = AlignValues[index]);
            };
            layoutRow.Add(_alignField);

            _fontField = new DropdownPill(FontLabels, 0);
            _fontField.style.width = 110f;
            _fontField.Changed += picked =>
            {
                int index = FontLabels.IndexOf(picked);
                if (index >= 0) EditSelected("font", s => s.Font = FontValues[index]);
            };
            layoutRow.Add(_fontField);
            _styleFields.Add(layoutRow);

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
            _resetButton.tooltip = "Styles are saved by banner name, just for you.";
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

        private VisualElement BuildPresetsCard()
        {
            var card = Card("Color presets", "paints every banner", out _);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginLeft = -3f;
            grid.style.marginRight = -3f;
            grid.style.marginBottom = -3f;

            foreach (var preset in ColorPresets)
                grid.Add(BuildPresetTile(preset));

            card.Add(grid);
            return card;
        }

        private VisualElement BuildPresetTile(ColorPreset preset)
        {
            var tile = new VisualElement();

            tile.style.flexBasis = Length.Percent(40f);
            tile.style.flexGrow = 1f;
            tile.style.backgroundColor = InsetColor;
            Margin(tile, 3f);
            Padding(tile, 6f, 6f);
            Radius(tile, 6f);
            tile.AddManipulator(new Clickable(() => ApplyPreset(preset)));
            HoverFeedback(tile);

            var palette = Row();
            palette.pickingMode = PickingMode.Ignore;
            palette.style.height = 22f;
            palette.style.overflow = Overflow.Hidden;
            Radius(palette, 5f);
            foreach (var color in preset.Colors)
            {
                var tone = new VisualElement { pickingMode = PickingMode.Ignore };
                tone.style.flexGrow = 1f;
                tone.style.height = 22f;
                tone.style.backgroundColor = color;
                palette.Add(tone);
            }
            tile.Add(palette);

            var name = new Label(preset.Name) { pickingMode = PickingMode.Ignore };
            name.style.fontSize = 11f;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = TextColor;
            name.style.marginTop = 5f;
            name.style.marginLeft = 1f;
            tile.Add(name);

            return tile;
        }

        private VisualElement BuildAdjustCard()
        {
            var card = Card("Banner colors", "only you", out _);

            _opacitySlider = new Slider("Opacity", HierarchyViewPrefs.MinOpacity, 1f);
            _opacitySlider.RegisterValueChangedCallback(e => HierarchyViewPrefs.BannerOpacity = e.newValue);
            card.Add(_opacitySlider);

            _saturationSlider = new Slider("Saturation", 0f, HierarchyViewPrefs.MaxSaturation);
            _saturationSlider.RegisterValueChangedCallback(e => HierarchyViewPrefs.BannerSaturation = e.newValue);
            card.Add(_saturationSlider);

            _lightnessSlider = new Slider("Lightness", -HierarchyViewPrefs.MaxLightness, HierarchyViewPrefs.MaxLightness);
            _lightnessSlider.RegisterValueChangedCallback(e => HierarchyViewPrefs.BannerLightness = e.newValue);
            card.Add(_lightnessSlider);

            _resetAdjustButton = FlatButton("Reset", false, HierarchyViewPrefs.ResetBannerAdjust);
            _resetAdjustButton.tooltip = "Shows the colors as they were picked.";
            _resetAdjustButton.style.marginTop = 6f;
            card.Add(_resetAdjustButton);

            return card;
        }

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

        private static GameObject Target()
        {
            var target = Selection.activeGameObject;
            return target != null && target.scene.IsValid() ? target : null;
        }

        private static bool IsBanner(GameObject go)
        {
            return go != null && go.scene.IsValid() && go.name.StartsWith(HeaderPrefix);
        }

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

        private static List<string> SceneBannerNames()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith(HeaderPrefix))
                .Select(t => CleanName(t.name))
                .GroupBy(CanonicalGroupKey)
                .Select(sameName => sameName.First())
                .Where(name => HierarchyGroupColors.Get(name)?.NoBackground != true)
                .ToList();
        }

        private void ApplyPreset(ColorPreset preset)
        {
            var names = SceneBannerNames();
            if (names.Count == 0)
            {
                Debug.Log("[Hierarchy Organizer] No banners with a background in this scene to paint.");
                return;
            }

            PushUndo("preset");
            HierarchyGroupColors.SetBackgrounds(names, index => preset.ColorAt(index, names.Count));
            Debug.Log($"[Hierarchy Organizer] Painted {names.Count} banner(s) with the {preset.Name} preset.");
        }

        private void PushUndo(string label, GameObject renamed = null, string oldName = null)
        {
            double now = EditorApplication.timeSinceStartup;

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

        private void AddGroup()
        {
            string clean = CleanName(_groupName.value ?? "");
            if (clean.Length == 0) return;

            var existing = FindHeaderGroups()
                .FirstOrDefault(g => CanonicalGroupKey(CleanName(g.name)) == CanonicalGroupKey(clean));
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[Hierarchy Organizer] A group named '{clean}' already exists, selected it instead.");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            var above = Target();
            int index = above != null && above.scene == scene ? above.transform.root.GetSiblingIndex() : -1;

            var group = new GameObject(BannerName(clean));
            Undo.RegisterCreatedObjectUndo(group, "New group");
            if (group.scene != scene) SceneManager.MoveGameObjectToScene(group, scene);
            if (index >= 0) group.transform.SetSiblingIndex(index);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = group;
        }

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
                HoverFeedback(button);
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
            _backgroundColor.SetValueWithoutNotify(HierarchyHeaderRenderer.HeaderColor(cleanName));
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

            _opacitySlider.SetValueWithoutNotify(HierarchyViewPrefs.BannerOpacity);
            _saturationSlider.SetValueWithoutNotify(HierarchyViewPrefs.BannerSaturation);
            _lightnessSlider.SetValueWithoutNotify(HierarchyViewPrefs.BannerLightness);
            _resetAdjustButton.SetEnabled(HierarchyViewPrefs.BannerAdjusted);
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

            var rootOnly = banners
                .SelectMany(b => b.transform.Cast<Transform>().Select(t => t.gameObject))
                .Where(NeedsSceneRoot)
                .ToArray();
            if (rootOnly.Length > 0)
            {
                issues.Add(new LintIssue
                {
                    Message = $"{rootOnly.Length} object(s) inside groups use DontDestroyOnLoad, which only works at the scene root.",
                    Objects = rootOnly,
                    FixLabel = "Unparent",
                    Fix = () => MoveToRoot(rootOnly),
                });
            }

            return issues;
        }

        private static void MoveToRoot(GameObject[] objects)
        {
            Undo.SetCurrentGroupName("Move to Scene Root");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var go in objects)
            {
                if (go == null || go.transform.parent == null) continue;

                Undo.SetTransformParent(go.transform, null, "Move to scene root");
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private static void RenameTo(GameObject go, string name)
        {
            if (go == null) return;

            Undo.RecordObject(go, "Rename group");
            go.name = name;
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorApplication.DirtyHierarchyWindowSorting();
        }

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
