using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static IsaDo3D.EnvironmentArt.IsaDo3DStyles;

namespace IsaDo3D.EnvironmentArt
{
    internal class IsaDo3DToolSettings : EditorWindow
    {
        private const float MinWidth = 340f;
        private const float MaxWidth = 700f;

        // Teto do que a janela pode ter de borda, pro ajuste de altura nunca crescer sem parar
        private const float MaxChrome = 60f;

        // Nos blocos de programa o caminho começa sempre na mesma coluna, na linha do toggle e nas de baixo
        private const float NameColumn = 124f;
        private const float ExtrasIndent = 21f;

        // Os cartões, na ordem em que aparecem
        private const string SceneGroup = "Scene";
        private const string AssetsGroup = "Assets";
        private const string AppsGroup = "External apps";
        private static readonly string[] Groups = { SceneGroup, AssetsGroup, AppsGroup };

        private const string AutoDetectHint = "Found automatically the first time you open something, or set it here.";

        private class ToolEntry
        {
            public string Group;
            public string Id;
            public string Title;
            public string ParentId;
            public Action Open;

            // Programa de fora ganha um bloco só dele: o caminho na linha do toggle e os extras embaixo
            public SetupEntry App;
            public int AccentHex;
            public string ExtrasTitle;
            public SetupEntry[] Extras = new SetupEntry[0];
        }

        // Uma coisa a configurar num programa de fora, só aparece com a ferramenta dele ligada
        private class SetupEntry
        {
            public string Name;

            // Sem caminho a linha mostra só o Hint, com caminho o Hint vira a dica de quando ele não está salvo
            public Func<string> GetPath;
            public bool IsFolder;
            public string Hint;

            // Pra linha que só existe de vez em quando e mostra um texto que muda
            public Func<bool> Visible;
            public Func<string> GetText;

            public string Button;
            public Action Run;
        }

        private class SetupView
        {
            public SetupEntry Entry;
            public string ToolId;
            public VisualElement Row;
            public Label Status;
        }

        private class TileView
        {
            public ToolEntry Tool;
            public VisualElement Accent;
            public VisualElement Extras;
            public readonly List<SetupView> ExtraRows = new List<SetupView>();
        }

        private static readonly ToolEntry[] Tools =
        {
            new ToolEntry
            {
                Group = SceneGroup, Id = ToolIds.ObjectEditor, Title = "Object Editor", Open = PrefabPivotTool.Open,
            },
            new ToolEntry
            {
                Group = SceneGroup, Id = ToolIds.MaterialBatch, Title = "Material Batch", Open = MaterialBatchTool.Open,
            },
            new ToolEntry
            {
                Group = SceneGroup, Id = ToolIds.HierarchyOrganizer, Title = "Hierarchy Organizer",
                Open = HierarchyOrganizerTool.Open,
            },
            new ToolEntry
            {
                Group = SceneGroup, Id = ToolIds.HierarchyHeaders, ParentId = ToolIds.HierarchyOrganizer,
                Title = "Color headers in the Hierarchy",
            },
            new ToolEntry { Group = SceneGroup, Id = ToolIds.Overlay, Title = "Scene View toolbar" },

            new ToolEntry { Group = AssetsGroup, Id = ToolIds.TextureMaterial, Title = "Create Material from Texture" },

            new ToolEntry
            {
                Group = AppsGroup, Id = ToolIds.Blender, Title = "Blender", AccentHex = 0xBB7723,
                App = new SetupEntry
                {
                    GetPath = () => OpenInBlender.SavedPath, Hint = AutoDetectHint,
                    Button = "Set path", Run = OpenInBlender.ConfigureBlenderPath,
                },
                ExtrasTitle = "Add-ons",
                Extras = new[]
                {
                    new SetupEntry
                    {
                        Name = "Batex export",
                        GetPath = () => OpenInBlender.BatexExportFolder, IsFolder = true,
                        Button = "Set folder", Run = OpenInBlender.ConfigureBatexExportFolder,
                    },
                    new SetupEntry
                    {
                        // Não dá pra saber daqui se já está instalado no Blender, então a linha não afirma nada
                        Name = "Krita Bridge", Hint = "",
                        Button = "Install", Run = OpenInBlender.InstallKritaBridgeAddon,
                    },
                },
            },
            new ToolEntry
            {
                Group = AppsGroup, Id = ToolIds.Maya, Title = "Maya", AccentHex = 0x539BA5,
                App = new SetupEntry
                {
                    GetPath = () => OpenInMaya.SavedPath, Hint = AutoDetectHint,
                    Button = "Set path", Run = OpenInMaya.ConfigureMayaPath,
                },
            },
            new ToolEntry
            {
                Group = AppsGroup, Id = ToolIds.ImageEditor, Title = "Image editor", AccentHex = 0x7137A5,
                App = new SetupEntry
                {
                    GetPath = () => OpenInImageEditor.EditorPath,
                    Hint = "Opens with the app Windows uses for the file. Same setting as Preferences > External Tools > Image application.",
                    Button = "Set path", Run = OpenInImageEditor.ConfigureEditorPath,
                },
                Extras = new[]
                {
                    new SetupEntry
                    {
                        Name = "Reload on save",
                        Visible = () => OpenInImageEditor.WatchedCount > 0,
                        GetText = () => $"Watching {OpenInImageEditor.WatchedCount} texture(s)",
                        Button = "Stop", Run = OpenInImageEditor.StopWatching,
                    },
                },
            },
        };

        // Lido uma vez e de novo só quando o estado salvo muda
        private HashSet<string> _state;

        private VisualElement _page;
        private VisualElement _body;
        private readonly Dictionary<ToolEntry, Toggle> _toggles = new Dictionary<ToolEntry, Toggle>();
        private readonly Dictionary<ToolEntry, Button> _openButtons = new Dictionary<ToolEntry, Button>();
        private readonly List<SetupView> _setupRows = new List<SetupView>();
        private readonly List<TileView> _tiles = new List<TileView>();

        [MenuItem("Art Tools/Settings...", false, -1000)]
        public static void Open()
        {
            GetWindow<IsaDo3DToolSettings>(true, "Art Tools");
        }

        // Filho sem pai só acontece mexendo no arquivo na mão
        private static HashSet<string> LoadState()
        {
            var state = IsaDo3DToolState.Enabled();

            foreach (var tool in Tools)
                if (tool.ParentId != null && !state.Contains(tool.ParentId))
                    state.Remove(tool.Id);

            return state;
        }

        // GetWindow só põe o título ao criar, janela que já estava aberta ficava com o nome antigo
        private void OnEnable()
        {
            titleContent = new GUIContent("Art Tools");
            IsaDo3DToolState.Changed += ReloadState;
            OpenInImageEditor.WatchListChanged += Refresh;
        }

        private void OnDisable()
        {
            IsaDo3DToolState.Changed -= ReloadState;
            OpenInImageEditor.WatchListChanged -= Refresh;
        }

        // O caminho de um programa também muda fora daqui, com a janela aberta
        private void OnFocus()
        {
            Refresh();
        }

        private void ReloadState()
        {
            _state = LoadState();
            Refresh();
        }

        // ---------- Montagem ----------

        private void CreateGUI()
        {
            _toggles.Clear();
            _openButtons.Clear();
            _setupRows.Clear();
            _tiles.Clear();

            _page = Page(rootVisualElement);

            // Tudo dentro de um elemento só, é a altura dele que a janela copia. A janela também avisa,
            // pra conferir depois que ela muda de tamanho
            var body = new VisualElement();
            _body = body;
            body.RegisterCallback<GeometryChangedEvent>(FitHeight);
            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(FitHeight);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(FitHeight);
            _page.Add(body);

            body.Add(Header("Art Tools"));
            body.Add(BuildTopRow());
            foreach (var group in Groups) body.Add(BuildGroup(group));
            body.Add(BuildVersion());

            ReloadState();
        }

        private VisualElement BuildTopRow()
        {
            var row = Row();
            row.style.marginBottom = 8f;

            var saved = Note("Saved per user");
            saved.tooltip = "UserSettings/IsaDo3DTools.asset, outside Git.";
            saved.style.flexGrow = 1f;
            row.Add(saved);

            row.Add(LinkButton("Enable all", EnableAll));
            row.Add(LinkButton("Disable all", DisableAll));
            return row;
        }

        private VisualElement BuildGroup(string group)
        {
            var card = Card(group);

            foreach (var tool in Tools)
            {
                if (tool.Group != group) continue;
                card.Add(tool.App != null ? BuildTile(tool) : BuildToggleRow(tool));
            }

            // O cartão já tem a própria margem embaixo
            card[card.childCount - 1].style.marginBottom = 0f;
            return card;
        }

        private Toggle BuildToggle(ToolEntry tool)
        {
            var toggle = new Toggle { text = tool.Title };
            toggle.RegisterValueChangedCallback(e => SetTool(tool, e.newValue));

            _toggles[tool] = toggle;
            return toggle;
        }

        private VisualElement BuildToggleRow(ToolEntry tool)
        {
            var row = Row();
            row.style.marginLeft = tool.ParentId != null ? 18f : 0f;

            var toggle = BuildToggle(tool);
            toggle.style.flexGrow = 1f;
            row.Add(toggle);

            if (tool.Open != null)
            {
                var open = SmallButton("Open", tool.Open);
                open.style.width = 52f;
                row.Add(open);
                _openButtons[tool] = open;
            }

            return row;
        }

        // Um bloco por programa, com uma faixa da cor dele na esquerda que apaga quando está desligado
        private VisualElement BuildTile(ToolEntry tool)
        {
            var view = new TileView { Tool = tool };

            var tile = Row();
            tile.style.alignItems = Align.Stretch;
            tile.style.backgroundColor = InsetColor;
            tile.style.marginBottom = 6f;
            tile.style.overflow = Overflow.Hidden;
            Radius(tile, 6f);

            view.Accent = new VisualElement();
            view.Accent.style.width = 3f;
            tile.Add(view.Accent);

            var content = new VisualElement();
            content.style.flexGrow = 1f;
            content.style.flexBasis = 0f;
            Padding(content, 6f, 8f);
            tile.Add(content);

            var header = Row();
            var toggle = BuildToggle(tool);
            toggle.style.width = NameColumn;
            toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(toggle);

            var app = BuildSetup(tool, tool.App);
            app.Row.style.flexGrow = 1f;
            app.Row.style.flexBasis = 0f;
            header.Add(app.Row);
            content.Add(header);

            // Separados do caminho por uma linha, são do programa mas não são o programa
            view.Extras = new VisualElement();
            view.Extras.style.marginTop = 6f;
            view.Extras.style.marginLeft = ExtrasIndent;
            view.Extras.style.paddingTop = 4f;
            view.Extras.style.borderTopWidth = 1f;
            view.Extras.style.borderTopColor = CardBorder;

            if (tool.ExtrasTitle != null)
            {
                var title = Section(tool.ExtrasTitle);
                title.style.fontSize = 10f;
                title.style.marginTop = 2f;
                title.style.marginBottom = 0f;
                view.Extras.Add(title);
            }

            foreach (var extra in tool.Extras)
            {
                var row = BuildSetup(tool, extra);
                row.Row.style.marginTop = 4f;
                view.Extras.Add(row.Row);
                view.ExtraRows.Add(row);
            }

            content.Add(view.Extras);

            _tiles.Add(view);
            return tile;
        }

        // Nome (se tiver), o que está salvo e o botão que muda
        private SetupView BuildSetup(ToolEntry tool, SetupEntry setup)
        {
            var row = Row();

            if (setup.Name != null)
            {
                var name = new Label(setup.Name);
                name.style.color = TextColor;
                name.style.width = NameColumn - ExtrasIndent;
                row.Add(name);
            }

            var status = Note("");
            status.style.flexGrow = 1f;
            status.style.flexShrink = 1f;
            status.style.flexBasis = 0f;
            status.style.overflow = Overflow.Hidden;
            status.style.textOverflow = TextOverflow.Ellipsis;
            status.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(status);

            var button = SmallButton(setup.Button, () =>
            {
                setup.Run();
                Refresh();
            });
            button.style.width = 72f;
            button.style.marginLeft = 6f;
            row.Add(button);

            var view = new SetupView { Entry = setup, ToolId = tool.Id, Row = row, Status = status };
            _setupRows.Add(view);
            return view;
        }

        private static Button SmallButton(string text, Action onClick)
        {
            var button = FlatButton(text, false, onClick);
            button.style.height = 18f;
            button.style.fontSize = 11f;
            return button;
        }

        // Pacote vindo do git mostra o commit, é o que diz de verdade qual versão a pessoa tem
        private static VisualElement BuildVersion()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(IsaDo3DToolSettings).Assembly);
            string text = "";

            if (package != null)
            {
                bool fromGit = package.source == UnityEditor.PackageManager.PackageSource.Git
                               && package.git != null && !string.IsNullOrEmpty(package.git.hash);
                string origin = fromGit
                    ? package.git.hash.Substring(0, Mathf.Min(7, package.git.hash.Length))
                    : package.source.ToString().ToLowerInvariant();
                text = $"v{package.version} · {origin}";
            }

            var version = Note(text);
            version.style.fontSize = 10f;
            version.style.unityTextAlign = TextAnchor.MiddleRight;
            return version;
        }

        // A janela fica da altura do conteúdo, que muda quando uma linha de programa aparece ou some.
        // Só pelo minSize e maxSize: escrever no position jogava a janela pro canto da tela, porque o
        // position lido aqui não acompanha a janela quando ela é arrastada
        private void FitHeight(GeometryChangedEvent e)
        {
            float view = rootVisualElement.layout.height;
            float content = _body.layout.height + _page.resolvedStyle.paddingTop + _page.resolvedStyle.paddingBottom;
            if (float.IsNaN(view) || float.IsNaN(content) || view <= 0f || content <= 0f) return;

            // Travada, a janela tem a altura do minSize, e o que ela mostra a menos que isso é borda
            bool locked = Mathf.Abs(minSize.y - maxSize.y) < 1f;
            float height = Mathf.Ceil(locked ? minSize.y + content - view : content);
            height = Mathf.Clamp(height, content, content + MaxChrome);
            if (locked && Mathf.Abs(minSize.y - height) < 1f) return;

            // Mínimo acima do máximo antigo trava a janela, então quem cresce sobe o máximo antes
            if (height > maxSize.y) maxSize = new Vector2(MaxWidth, height);
            minSize = new Vector2(MinWidth, height);
            maxSize = new Vector2(MaxWidth, height);
        }

        // ---------- Atualização ----------

        private void Refresh()
        {
            if (_toggles.Count == 0) return;
            var state = _state ??= LoadState();

            foreach (var pair in _toggles)
            {
                var tool = pair.Key;
                bool parentOff = tool.ParentId != null && !state.Contains(tool.ParentId);

                pair.Value.SetValueWithoutNotify(state.Contains(tool.Id) && !parentOff);
                pair.Value.SetEnabled(!parentOff);
            }

            foreach (var pair in _openButtons)
                pair.Value.SetEnabled(state.Contains(pair.Key.Id));

            foreach (var view in _setupRows)
            {
                bool shown = IsShown(view, state);
                SetVisible(view.Row, shown);
                if (shown) RefreshStatus(view);
            }

            foreach (var tile in _tiles)
            {
                bool on = state.Contains(tile.Tool.Id);
                tile.Accent.style.backgroundColor = on ? Hex(tile.Tool.AccentHex) : CardBorder;
                SetVisible(tile.Extras, on && tile.ExtraRows.Any(row => IsShown(row, state)));
            }
        }

        private static bool IsShown(SetupView view, HashSet<string> state)
        {
            return state.Contains(view.ToolId) && (view.Entry.Visible == null || view.Entry.Visible());
        }

        private static void RefreshStatus(SetupView view)
        {
            var setup = view.Entry;
            var status = view.Status;
            status.style.color = MutedColor;

            if (setup.GetPath == null)
            {
                status.text = setup.GetText != null ? setup.GetText() : setup.Hint;
                status.tooltip = "";
                return;
            }

            string path = setup.GetPath();
            if (string.IsNullOrEmpty(path))
            {
                status.text = "Not set";
                status.tooltip = setup.Hint ?? "";
                return;
            }

            bool exists = setup.IsFolder ? Directory.Exists(path) : File.Exists(path);
            status.text = exists ? Shorten(path) : "Not found";
            status.tooltip = path;
            if (!exists) status.style.color = Danger;
        }

        // O fim do caminho é o que diferencia uma instalação da outra
        private static string Shorten(string path)
        {
            var parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
            return parts.Length <= 2 ? path : $"…/{parts[parts.Length - 2]}/{parts[parts.Length - 1]}";
        }

        // ---------- Estado ----------

        // Salvar dispara o Changed, que relê o estado e atualiza a janela
        private void SetTool(ToolEntry tool, bool on)
        {
            var state = _state ??= LoadState();

            if (on) state.Add(tool.Id);
            else Disable(tool.Id, state);

            IsaDo3DToolState.SetEnabled(state);
        }

        private void EnableAll()
        {
            IsaDo3DToolState.SetEnabled(Tools.Select(tool => tool.Id).ToList());
        }

        private void DisableAll()
        {
            IsaDo3DToolState.SetEnabled(new string[0]);
        }

        private static void Disable(string id, HashSet<string> state)
        {
            state.Remove(id);
            foreach (var child in Tools.Where(t => t.ParentId == id))
                Disable(child.Id, state);
        }
    }
}
