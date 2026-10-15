using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal class IsaDo3DToolSettings : EditorWindow
    {
        private class ToolEntry
        {
            public string Id;
            public string Title;
            public string Summary;
            public string ParentId;
            public ChoiceOption[] Options;
        }

        private class ChoiceOption
        {
            public string Label;
            public string Summary;
            public string[] Ids;
        }

        private static readonly Dictionary<ToolEntry, string[]> OptionLabels = new Dictionary<ToolEntry, string[]>();

        private static string[] LabelsOf(ToolEntry tool)
        {
            if (OptionLabels.TryGetValue(tool, out var labels)) return labels;

            labels = tool.Options.Select(o => o.Label).ToArray();
            OptionLabels[tool] = labels;
            return labels;
        }

        private static readonly ToolEntry[] Tools =
        {
            new ToolEntry
            {
                Id = ToolIds.ObjectEditor,
                Title = "Object Editor",
                Summary = "Reposicionar pivô e encaixar objeto numa superfície de referência.",
            },
            new ToolEntry
            {
                Id = ToolIds.MaterialBatch,
                Title = "Material Batch",
                Summary = "Aplica um material a todos os objetos selecionados.",
            },
            new ToolEntry
            {
                Id = ToolIds.ComponentCopy,
                Title = "Copiar Componentes",
                Summary = "Copia os componentes de um prefab para objetos selecionados na cena.",
            },
            new ToolEntry
            {
                Id = ToolIds.HierarchyOrganizer,
                Title = "Hierarchy Organizer",
                Summary = "Cria grupos na Hierarchy.",
            },
            new ToolEntry
            {
                Id = ToolIds.HierarchyHeaders,
                ParentId = ToolIds.HierarchyOrganizer,
                Title = "Colorir os cabeçalhos na Hierarchy",
                Summary = "Desenha os grupos coloridos na Hierarchy, com botão de selecionar os filhos.",
            },
            new ToolEntry
            {
                Id = ToolIds.LodBuilder,
                Title = "LOD Group Builder",
                Summary = "Monta o LODGroup a partir da hierarquia {base} > {base}_LOD0/1/2 que o Auto LOD do " +
                          "Blender exporta.",
            },
            new ToolEntry
            {
                Id = ToolIds.MissingScripts,
                Title = "Missing Scripts",
                Summary = "Acha e remove componentes de script ausente na cena aberta. Menu: Isa do 3D/QA.",
            },
            new ToolEntry
            {
                Title = "Software 3D",
                Summary = "Pra onde vai a mesh quando eu mando ela pra fora.",
                Options = new[]
                {
                    new ChoiceOption
                    {
                        Label = "Nenhum",
                        Summary = "Sem integração 3D. Os menus Blender e Maya ficam cinza.",
                        Ids = new string[0],
                    },
                    new ChoiceOption
                    {
                        Label = "Blender",
                        Summary = "Abre a mesh selecionada no Blender. Você configura o caminho do executável e a " +
                                  "pasta de exportação do Batex. Também instala o Krita Bridge.",
                        Ids = new[] { ToolIds.Blender },
                    },
                    new ChoiceOption
                    {
                        Label = "Maya",
                        Summary = "Abre a mesh selecionada no Maya e configura o caminho do executável. " +
                                  "Menu: Isa do 3D/Maya.",
                        Ids = new[] { ToolIds.Maya },
                    },
                    new ChoiceOption
                    {
                        Label = "Os dois",
                        Summary = "Os dois menus ao mesmo tempo. Serve pra quem usa os dois de verdade " +
                                  "(Maya pra personagem, Blender pra prop), não é o caminho normal.",
                        Ids = new[] { ToolIds.Blender, ToolIds.Maya },
                    },
                },
            },
            new ToolEntry
            {
                Id = ToolIds.Krita,
                Title = "Krita",
                Summary = "Abre a textura selecionada no Krita e reimporta sozinho " +
                          "quando ela for salva lá. Acesso: Isa do 3D/Krita ou clique direito na textura.",
            },
            new ToolEntry
            {
                Id = ToolIds.Overlay,
                Title = "Barra na Scene View",
                Summary = "Overlay flutuante com os atalhos. Nem todas as ferramentas ficam na Scene View.",
            },
        };

        private Vector2 _scroll;

        // Lido uma vez e de novo só quando o estado salvo muda, o OnGUI roda a cada evento
        private HashSet<string> _state;
        private bool _changed;

        [MenuItem("Isa do 3D/Ferramentas...", false, -1000)]
        public static void Open()
        {
            var window = GetWindow<IsaDo3DToolSettings>(true, "Ferramentas da Isa do 3D");
            window.minSize = new Vector2(460f, 420f);
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

        private void OnEnable()
        {
            ReloadState();
            IsaDo3DToolState.Changed += ReloadState;
        }

        private void OnDisable()
        {
            IsaDo3DToolState.Changed -= ReloadState;
        }

        private void ReloadState()
        {
            _state = LoadState();
            Repaint();
        }

        private void OnGUI()
        {
            _changed = false;
            var state = _state ??= LoadState();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                foreach (var tool in Tools)
                {
                    if (tool.Options != null) DrawChoice(tool, state);
                    else DrawTool(tool, state);
                }
            }

            EditorGUILayout.Space();
            DrawFooter(state);

            if (_changed) IsaDo3DToolState.SetEnabled(state);
        }

        private void DrawTool(ToolEntry tool, HashSet<string> state)
        {
            bool isChild = tool.ParentId != null;
            bool parentOff = isChild && !state.Contains(tool.ParentId);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (isChild) GUILayout.Space(18f);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                using (new EditorGUI.DisabledScope(parentOff))
                {
                    bool on = state.Contains(tool.Id) && !parentOff;
                    bool next = EditorGUILayout.ToggleLeft(tool.Title, on, EditorStyles.boldLabel);

                    if (next != on)
                    {
                        if (next) state.Add(tool.Id);
                        else Disable(tool.Id, state);
                        _changed = true;
                    }

                    EditorGUILayout.LabelField(tool.Summary, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawChoice(ToolEntry tool, HashSet<string> state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(tool.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(tool.Summary, EditorStyles.wordWrappedMiniLabel);

                int current = SelectedOption(tool, state);
                int next = GUILayout.Toolbar(current, LabelsOf(tool));

                if (next != current)
                {
                    foreach (var option in tool.Options)
                        foreach (var id in option.Ids)
                            Disable(id, state);

                    foreach (var id in tool.Options[next].Ids)
                        state.Add(id);
                    _changed = true;
                }

                EditorGUILayout.LabelField(tool.Options[next].Summary, EditorStyles.wordWrappedMiniLabel);
            }
        }

        // A opção certa é a que tem ligado exatamente os ids dela e nenhum das outras
        private static int SelectedOption(ToolEntry tool, HashSet<string> state)
        {
            for (int i = 0; i < tool.Options.Length; ++i)
            {
                var ids = tool.Options[i].Ids;
                bool matches = true;

                foreach (var option in tool.Options)
                    foreach (var id in option.Ids)
                        if (state.Contains(id) != (System.Array.IndexOf(ids, id) >= 0)) matches = false;

                if (matches) return i;
            }

            return 0;
        }

        private static void Disable(string id, HashSet<string> state)
        {
            state.Remove(id);
            foreach (var child in Tools.Where(t => t.ParentId == id))
                Disable(child.Id, state);
        }

        private void DrawFooter(HashSet<string> state)
        {
            if (state.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Com tudo desligado os menus do pacote ficam cinza, só este continua funcionando.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ligar tudo"))
                {
                    var all = IdsToEnableAll(state).ToList();
                    state.Clear();
                    state.UnionWith(all);
                    _changed = true;
                }

                if (GUILayout.Button("Desligar tudo"))
                {
                    state.Clear();
                    _changed = true;
                }
            }

            EditorGUILayout.LabelField(
                "Vale só pra você, fica em UserSettings/IsaDo3DTools.asset (fora do Git).",
                EditorStyles.miniLabel);
        }

        // Escolha mantém o que está escolhido, e sem escolha fica com a primeira opção que liga algo
        private static IEnumerable<string> IdsToEnableAll(HashSet<string> state)
        {
            foreach (var tool in Tools)
            {
                if (tool.Options == null)
                {
                    yield return tool.Id;
                    continue;
                }

                var option = tool.Options[SelectedOption(tool, state)];
                if (option.Ids.Length == 0) option = tool.Options.First(o => o.Ids.Length > 0);

                foreach (var id in option.Ids)
                    yield return id;
            }
        }
    }
}
