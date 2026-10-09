using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    // Única parte que compila sempre, senão não daria pra ligar a primeira ferramenta
    internal class IsaDo3DToolSettings : EditorWindow
    {
        internal const string DefinePrefix = "ISADO3D_";

        private class ToolEntry
        {
            public string Define;
            public string Title;
            public string Summary;
            public string ParentDefine;
            public ChoiceOption[] Options;
        }

        private class ChoiceOption
        {
            public string Label;
            public string Summary;
            public string[] Defines;
        }

        private static readonly ToolEntry[] Tools =
        {
            new ToolEntry
            {
                Define = DefinePrefix + "OBJECT_EDITOR",
                Title = "Object Editor",
                Summary = "Reposicionar pivô e encaixar objeto numa superfície de referência.",
                          
            },
            new ToolEntry
            {
                Define = DefinePrefix + "MATERIAL_BATCH",
                Title = "Material Batch",
                Summary = "Aplica um material a todos os objetos selecionados",
                          
            },
            new ToolEntry
            {
                Define = DefinePrefix + "HIERARCHY_ORGANIZER",
                Title = "Hierarchy Organizer",
                Summary = "Cria grupos na hierarquia",
                          
            },
            new ToolEntry
            {
                Define = DefinePrefix + "HIERARCHY_HEADERS",
                ParentDefine = DefinePrefix + "HIERARCHY_ORGANIZER",
                Title = "Colorir os cabeçalhos na Hierarchy",
                Summary = "Desenha os grupos coloridos direto na janela Hierarchy, com botão de selecionar os " +
                          "filhos. Muda a cara da Hierarchy pra todo mundo que abrir o projeto.",
            },
            new ToolEntry
            {
                Define = DefinePrefix + "LOD_BUILDER",
                Title = "LOD Group Builder",
                Summary = "Monta o LODGroup a partir da hierarquia {base} > {base}_LOD0/1/2 que o Auto LOD do " +
                          "Blender exporta.",
            },
            new ToolEntry
            {
                Define = DefinePrefix + "MISSING_SCRIPTS",
                Title = "Missing Scripts",
                Summary = "Acha e remove componentes de script ausente na cena aberta. Menu: Isa do 3D/QA.",
            },
            new ToolEntry
            {
                Title = "Software 3D",
                Summary = "Pra onde vai a mesh quando eu mando ela pra fora. ",
                Options = new[]
                {
                    new ChoiceOption
                    {
                        Label = "Nenhum",
                        Summary = "Sem integração 3D. Os menus Blender e Maya não existem neste projeto.",
                        Defines = new string[0],
                    },
                    new ChoiceOption
                    {
                        Label = "Blender",
                        Summary = "Abre a mesh selecionada no Blender. Você configura o caminho do executável e a " +
                                  "pasta de exportação do Batex, Também instala o Krita Bridge.",
                        Defines = new[] { DefinePrefix + "BLENDER" },
                    },
                    new ChoiceOption
                    {
                        Label = "Maya",
                        Summary = "Abre a mesh selecionada no Maya e configura o caminho do executável. " +
                                  "Menu: Isa do 3D/Maya.",
                        Defines = new[] { DefinePrefix + "MAYA" },
                    },
                    new ChoiceOption
                    {
                        Label = "Os dois",
                        Summary = "Os dois menus ao mesmo tempo. Serve pra time que usa os dois de verdade " +
                                  "(Maya pra personagem, Blender pra prop), não é o caminho normal.",
                        Defines = new[] { DefinePrefix + "BLENDER", DefinePrefix + "MAYA" },
                    },
                },
            },
            new ToolEntry
            {
                Define = DefinePrefix + "KRITA",
                Title = "Krita",
                Summary = "Abre a textura selecionada no Krita e reimporta sozinho " +
                          "quando ela for salva lá. Acesso: Isa do 3D/Krita ou clique direito na textura.",
            },
            new ToolEntry
            {
                Define = DefinePrefix + "OVERLAY",
                Title = "Barra na Scene View",
                Summary = "Overlay flutuante com os atalhos. Nem todas as ferramentas ficam na Scene View.",
            },
        };

        private HashSet<string> _pending;
        private Vector2 _scroll;

        [MenuItem("Isa do 3D/Ferramentas...", false, -1000)]
        public static void Open()
        {
            var window = GetWindow<IsaDo3DToolSettings>(true, "Ferramentas da Isa do 3D");
            window.minSize = new Vector2(460f, 420f);
        }

        private void OnEnable()
        {
            _pending = LoadState();
        }

        // Filho sem pai só acontece mexendo nos defines na mão
        private static HashSet<string> LoadState()
        {
            var state = new HashSet<string>(EnabledDefines());

            foreach (var tool in Tools)
                if (tool.ParentDefine != null && !state.Contains(tool.ParentDefine))
                    state.Remove(tool.Define);

            return state;
        }

        private void OnGUI()
        {
            if (_pending == null) _pending = LoadState();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                foreach (var tool in Tools)
                {
                    if (tool.Options != null) DrawChoice(tool);
                    else DrawTool(tool);
                }
            }

            EditorGUILayout.Space();
            DrawFooter();
        }

        private void DrawTool(ToolEntry tool)
        {
            bool isChild = tool.ParentDefine != null;
            bool parentOff = isChild && !_pending.Contains(tool.ParentDefine);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (isChild) GUILayout.Space(18f);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                using (new EditorGUI.DisabledScope(parentOff))
                {
                    bool on = _pending.Contains(tool.Define) && !parentOff;
                    bool next = EditorGUILayout.ToggleLeft(tool.Title, on, EditorStyles.boldLabel);

                    if (next != on)
                    {
                        if (next) _pending.Add(tool.Define);
                        else Disable(tool.Define);
                    }

                    EditorGUILayout.LabelField(tool.Summary, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }
        private void DrawChoice(ToolEntry tool)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(tool.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(tool.Summary, EditorStyles.wordWrappedMiniLabel);

                int current = SelectedOption(tool, _pending);
                int next = GUILayout.Toolbar(current, tool.Options.Select(o => o.Label).ToArray());

                if (next != current)
                {
                    foreach (var define in tool.Options.SelectMany(o => o.Defines))
                        Disable(define);

                    foreach (var define in tool.Options[next].Defines)
                        _pending.Add(define);
                }

                EditorGUILayout.LabelField(tool.Options[next].Summary, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static int SelectedOption(ToolEntry tool, HashSet<string> state)
        {
            var active = new HashSet<string>(tool.Options.SelectMany(o => o.Defines).Where(state.Contains));

            for (int i = 0; i < tool.Options.Length; ++i)
                if (active.SetEquals(tool.Options[i].Defines))
                    return i;

            return 0;
        }
        private void Disable(string define)
        {
            _pending.Remove(define);
            foreach (var child in Tools.Where(t => t.ParentDefine == define))
                Disable(child.Define);
        }

        private void DrawFooter()
        {
            var current = LoadState();
            bool dirty = !current.SetEquals(_pending);

            if (_pending.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Com tudo desligado o pacote fica inerte neste projeto, só este menu continua existindo.",
                    MessageType.Info);
            }

            if (dirty)
            {
                EditorGUILayout.HelpBox(
                    "Aplicar recompila os scripts do projeto (alguns segundos).",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ligar tudo"))
                    _pending = new HashSet<string>(AllDefines());

                if (GUILayout.Button("Desligar tudo"))
                    _pending.Clear();

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("Descartar"))
                        _pending = current;

                    if (GUILayout.Button("Aplicar", GUILayout.Width(90f)))
                        Apply();
                }
            }

            EditorGUILayout.LabelField(
                $"Defines gravados em: {ActiveTargetName()}",
                EditorStyles.miniLabel);
        }

        private void Apply()
        {
            if (!TryGetActiveTarget(out var target)) return;

            PlayerSettings.GetScriptingDefineSymbols(target, out string[] existing);

            // Só os ISADO3D_ são desta janela, o resto é do projeto
            var kept = existing.Where(d => !d.StartsWith(DefinePrefix));
            var updated = kept.Concat(_pending).Distinct().OrderBy(d => d).ToArray();

            PlayerSettings.SetScriptingDefineSymbols(target, updated);
            MarkSeen();
        }

        private static IEnumerable<string> EnabledDefines()
        {
            if (!TryGetActiveTarget(out var target)) return Enumerable.Empty<string>();

            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            var known = new HashSet<string>(AllDefines());
            return defines.Where(known.Contains);
        }
        
        private static IEnumerable<string> AllDefines()
        {
            return Tools
                .SelectMany(t => t.Options != null ? t.Options.SelectMany(o => o.Defines) : new[] { t.Define })
                .Distinct();
        }
        
        private static bool TryGetActiveTarget(out NamedBuildTarget target)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                target = default;
                return false;
            }

            target = NamedBuildTarget.FromBuildTargetGroup(group);
            return true;
        }

        private static string ActiveTargetName()
        {
            return TryGetActiveTarget(out var target) ? target.TargetName : "(build target desconhecido)";
        }

        private static string SeenKey => "IsaDo3D.ToolSettings.Seen." + Application.dataPath;

        private static void MarkSeen() => EditorPrefs.SetBool(SeenKey, true);

        [InitializeOnLoadMethod]
        private static void HintOnFirstLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(SeenKey, false)) return;

                MarkSeen();
                if (EnabledDefines().Any()) return;

                Debug.Log("[Isa do 3D] Nenhuma ferramenta ligada neste projeto. " +
                          "Menu Isa do 3D/Ferramentas... pra escolher quais entram.");
            };
        }
    }
}
