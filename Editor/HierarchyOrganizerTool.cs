using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_5_OR_NEWER
using UH = Unity.Hierarchy;
using UHE = Unity.Hierarchy.Editor;
using UIE = UnityEngine.UIElements;
#endif

namespace IsaDo3D.EnvironmentArt
{
    public class HierarchyOrganizerTool : EditorWindow
    {
        public const string HeaderPrefix = "---";

        private class DefaultGroup
        {
            public string Name;
            public Color Color;
            public string Tooltip;
        }

        // Na ordem em que entram na cena
        private static readonly DefaultGroup[] DefaultGroups =
        {
            new DefaultGroup
            {
                Name = "CORE", Color = FromHex(0xe50000), // Vermelho
                Tooltip = "Câmeras, managers e sistemas centrais da cena.",
            },
            new DefaultGroup
            {
                Name = "LIGHTING", Color = FromHex(0xff8d00), // Laranja
                Tooltip = "Luzes, reflection probes, light probes e volumes de iluminação.",
            },
            new DefaultGroup
            {
                Name = "VFX", Color = FromHex(0xffee00), // Amarelo
                Tooltip = "Sistemas de partículas e efeitos visuais.",
            },
            new DefaultGroup
            {
                Name = "AUDIO", Color = FromHex(0x00811f), // Verde
                Tooltip = "Fontes de áudio, ambiências e música.",
            },
            new DefaultGroup
            {
                Name = "UI", Color = FromHex(0x004cff), // Azul
                Tooltip = "Canvases e elementos de interface.",
            },
            new DefaultGroup
            {
                Name = "GAMEPLAY", Color = FromHex(0x001a98), // Azul-marinho
                Tooltip = "Objetos de level design: spawns, interativos (chests, shrines, breakables) e triggers.",
            },
            new DefaultGroup
            {
                Name = "ENVIRONMENT", Color = FromHex(0x290d7a), // Violeta
                Tooltip = "Cenário estático: terreno, arquitetura e meshes marcados como Static.",
            },
            new DefaultGroup
            {
                Name = "PROPS", Color = FromHex(0x800080), // Roxo
                Tooltip = "Objetos dinâmicos ou interativos do set dressing.",
            },
        };

        private static readonly string[] GameplaySubGroups = { "Spawns", "Interactables", "Triggers" };

        private static Color FromHex(int hex)
        {
            return new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);
        }

        private static DefaultGroup FindDefaultGroup(string cleanName)
        {
            var key = cleanName.ToUpperInvariant();
            return DefaultGroups.FirstOrDefault(g => g.Name == key);
        }

        public static string TooltipFor(string cleanName)
        {
            return FindDefaultGroup(cleanName)?.Tooltip ?? "Grupo personalizado.";
        }

        internal static bool TryGetDefaultColor(string cleanName, out Color color)
        {
            var group = FindDefaultGroup(cleanName);
            color = group?.Color ?? default;
            return group != null;
        }

        private static GUIStyle _groupButtonStyle;
        private static GUIStyle GroupButtonStyle =>
            _groupButtonStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

        private Vector2 _scroll;
        private bool _simulateOnly;

        [MenuItem("Isa do 3D/Cena/Hierarchy Organizer", true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.HierarchyOrganizer);

        [MenuItem("Isa do 3D/Cena/Hierarchy Organizer")]
        public static void Open()
        {
            GetWindow<HierarchyOrganizerTool>("Hierarchy Organizer");
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnHierarchyChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            int selectedCount = SelectedSceneObjects().Length;

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Grupos padrão", IsaDo3DStyles.Title);
                    EditorGUILayout.LabelField(
                        "Cria contêineres vazios na raiz da cena (transform zerado) pra agrupar os objetos por função. " +
                        "Qualquer objeto com nome começando em \"---\" vira um cabeçalho colorido na Hierarchy.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    if (GUILayout.Button("Criar grupos padrão na cena"))
                        CreateDefaultGroups();
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Mover seleção pra um grupo", IsaDo3DStyles.Title);
                    EditorGUILayout.LabelField(
                        $"Objetos selecionados: {selectedCount}",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    DrawGroupButtons(FindHeaderGroups(), selectedCount > 0);
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Organizar automaticamente", IsaDo3DStyles.Title);
                    EditorGUILayout.LabelField(
                        "Classifica os objetos soltos na raiz da cena e move cada um pro grupo certo. " +
                        "Objetos sem classificação ficam onde estão.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    _simulateOnly = EditorGUILayout.Toggle(
                        new GUIContent("Somente simular", "Mostra no Console pra onde cada objeto iria, sem mover nada."),
                        _simulateOnly);

                    if (GUILayout.Button("Organizar objetos soltos da raiz"))
                        AutoOrganize();
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Renomear em lote", IsaDo3DStyles.Title);
                    EditorGUILayout.LabelField(
                        "Limpa os sufixos \"(n)\" que a Unity cria ao duplicar e renumera os objetos selecionados. " +
                        "Números que fazem parte do nome do asset (exemplo: Isa_Suja_02) são preservados.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    using (new EditorGUI.DisabledScope(selectedCount == 0))
                    {
                        if (GUILayout.Button($"Normalizar nomes da seleção ({selectedCount})"))
                            NormalizeSelectedNames();
                    }
                }
            }
        }

        private static void DrawGroupButtons(List<GameObject> groups, bool hasSelection)
        {
            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhum grupo na cena ainda. Crie os grupos padrão acima.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                foreach (var group in groups)
                {
                    var cleanName = CleanName(group.name);
                    var text = $"{cleanName}  ({group.transform.childCount})";
                    if (DrawColoredGroupButton(cleanName, text, TooltipFor(cleanName)))
                        MoveSelectionToGroup(group);
                }
            }
        }

        // Prefab do Project não entra, e com topLevel o filho selecionado junto do pai fica no pai
        private static GameObject[] SelectedSceneObjects(bool topLevel = false)
        {
            var objects = topLevel ? Selection.GetFiltered<GameObject>(SelectionMode.TopLevel) : Selection.gameObjects;
            return objects.Where(go => go.scene.IsValid()).ToArray();
        }

        public static string CleanName(string headerName)
        {
            return headerName.Trim('-', ' ');
        }

        private static List<GameObject> FindHeaderGroups()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(go => go.name.StartsWith(HeaderPrefix))
                .ToList();
        }

        private static Dictionary<string, GameObject> BuildGroupCache()
        {
            return FindHeaderGroups()
                .GroupBy(g => CleanName(g.name).ToUpperInvariant())
                .ToDictionary(gr => gr.Key, gr => gr.First());
        }

        private static void CreateDefaultGroups()
        {
            Undo.SetCurrentGroupName("Criar Grupos Padrão");
            int undoGroup = Undo.GetCurrentGroup();

            var cache = BuildGroupCache();

            int created = 0, reordered = 0, index = 0;
            foreach (var group in DefaultGroups)
            {
                int before = cache.Count;
                var go = GetOrCreateGroup(group.Name, cache);
                bool isNew = cache.Count > before;
                if (isNew) created++;

                if (go.transform.GetSiblingIndex() != index)
                {
                    // Grupo recém-criado vai pro lugar dele sem contar como reordenado
                    if (!isNew) reordered++;
#if UNITY_2022_3_OR_NEWER
                    // Undo.SetSiblingIndex pra reordenação entrar no Ctrl+Z
                    Undo.SetSiblingIndex(go.transform, index, "Reordenar grupo");
#else
                    // Não existe Undo.SetSiblingIndex na 2021.3
                    Undo.RecordObject(go.transform, "Reordenar grupo");
                    go.transform.SetSiblingIndex(index);
#endif
                }
                index++;
            }

            int subCreated = 0;
            foreach (var sub in GameplaySubGroups)
            {
                GetOrCreateGroupPath("GAMEPLAY/" + sub, cache, out int subsCreated);
                subCreated += subsCreated;
            }

            if (created + reordered + subCreated > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log($"[Hierarchy Organizer] {created} grupo(s) e {subCreated} subgrupo(s) criado(s), {reordered} reordenado(s).");
            }
            else
            {
                Debug.Log("[Hierarchy Organizer] Grupos padrão já existiam e estão na ordem.");
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        // Nome ganha de componente (chest tem mesh, mas é gameplay)
        private static readonly (string Keyword, string Destination)[] NameRules =
        {
            ("spawn", "GAMEPLAY/Spawns"),
            ("patrol", "GAMEPLAY/Spawns"),
            ("trigger", "GAMEPLAY/Triggers"),
            ("checkpoint", "GAMEPLAY/Triggers"),
            ("chest", "GAMEPLAY/Interactables"),
            ("shrine", "GAMEPLAY/Interactables"),
            ("breakable", "GAMEPLAY/Interactables"),
            ("door", "GAMEPLAY/Interactables"),
            ("lever", "GAMEPLAY/Interactables"),
            ("interact", "GAMEPLAY/Interactables"),
        };

        // Por palavra, senão Outdoor_Lamp vira porta
        private static string[] NameTokens(string name)
        {
            var spaced = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant();
            return Regex.Split(spaced, "[^a-z0-9]+");
        }

        // Mesh antes de luz: tocha (mesh + light) é prop
        private static string ClassifyObject(GameObject go)
        {
            var tokens = NameTokens(go.name);
            foreach (var (keyword, destination) in NameRules)
                if (tokens.Any(t => t.StartsWith(keyword))) return destination;

            if (go.GetComponentInChildren<Terrain>(true) != null) return "ENVIRONMENT";
            if (go.GetComponentInChildren<Canvas>(true) != null) return "UI";
            if (go.GetComponentInChildren<Camera>(true) != null) return "CORE";
            if (go.GetComponentInChildren<ParticleSystem>(true) != null) return "VFX";

            bool hasMesh = go.GetComponentInChildren<MeshRenderer>(true) != null
                        || go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
            if (hasMesh)
            {
                bool isStatic = go.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.isStatic);
                return isStatic ? "ENVIRONMENT" : "PROPS";
            }

            if (go.GetComponentInChildren<Light>(true) != null
                || go.GetComponentInChildren<ReflectionProbe>(true) != null
                || go.GetComponentInChildren<LightProbeGroup>(true) != null) return "LIGHTING";
            if (go.GetComponentInChildren<AudioSource>(true) != null) return "AUDIO";

            return null;
        }

        private static GameObject GetOrCreateGroup(string name, Dictionary<string, GameObject> cache)
        {
            var key = name.ToUpperInvariant();
            if (cache.TryGetValue(key, out var existing)) return existing;

            var group = new GameObject($"{HeaderPrefix} {name} {HeaderPrefix}");
            Undo.RegisterCreatedObjectUndo(group, "Criar grupo");
            cache[key] = group;
            return group;
        }

        private static GameObject GetOrCreateGroupPath(string path, Dictionary<string, GameObject> cache, out int subgroupsCreated)
        {
            subgroupsCreated = 0;
            var parts = path.Split('/');
            var current = GetOrCreateGroup(parts[0], cache).transform;

            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.Find(parts[i]);
                if (child == null)
                {
                    var sub = new GameObject(parts[i]);
                    Undo.RegisterCreatedObjectUndo(sub, "Criar subgrupo");
                    Undo.SetTransformParent(sub.transform, current, "Posicionar subgrupo");
                    child = sub.transform;
                    subgroupsCreated++;
                }
                current = child;
            }

            return current.gameObject;
        }

        private void AutoOrganize()
        {
            Undo.SetCurrentGroupName("Organizar Hierarchy Automaticamente");
            int undoGroup = Undo.GetCurrentGroup();

            var cache = BuildGroupCache();

            var loose = SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(go => !go.name.StartsWith(HeaderPrefix))
                .ToArray();

            int moved = 0, skipped = 0;
            var report = new StringBuilder();

            foreach (var go in loose)
            {
                string groupName = ClassifyObject(go);
                if (groupName == null)
                {
                    skipped++;
                    report.AppendLine($"  (sem regra) {go.name}");
                    continue;
                }

                report.AppendLine($"  {go.name} vai pra {groupName}");
                if (!_simulateOnly)
                    Undo.SetTransformParent(go.transform, GetOrCreateGroupPath(groupName, cache, out _).transform, "Mover pro grupo");
                moved++;
            }

            if (!_simulateOnly && moved > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Undo.CollapseUndoOperations(undoGroup);

            string summary = _simulateOnly
                ? $"[Hierarchy Organizer] Simulação: {moved} objeto(s) seriam movidos, {skipped} sem classificação."
                : $"[Hierarchy Organizer] {moved} objeto(s) organizados, {skipped} deixados na raiz (sem classificação).";
            Debug.Log(summary + "\n" + report);
        }

        // O _02 é do asset, só o " (n)" é da Unity
        private static string BaseName(string name)
        {
            var match = Regex.Match(name.Trim(), @"^(.*?)(?:\s*\(\d+\))*$");
            var baseName = match.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(baseName) ? name.Trim() : baseName;
        }

        private static void NormalizeSelectedNames()
        {
            var selection = SelectedSceneObjects()
                .Where(go => !go.name.StartsWith(HeaderPrefix))
                .OrderBy(go => go.transform.GetSiblingIndex())
                .ToList();
            if (selection.Count == 0) return;

            Undo.SetCurrentGroupName("Normalizar Nomes");
            int undoGroup = Undo.GetCurrentGroup();

            var groups = selection.GroupBy(go => BaseName(go.name)).ToList();

            // Sem isso "Rock" + "Rock (1)" viram Rock_01 e batem com um Rock_01 que já existia
            var selected = new HashSet<GameObject>(selection);
            var taken = new HashSet<string>(selection
                .GroupBy(go => (object)go.transform.parent ?? go.scene)
                .Select(g => g.First())
                .SelectMany(go => go.transform.parent != null
                    ? go.transform.parent.Cast<Transform>().Select(t => t.gameObject)
                    : go.scene.IsValid() ? go.scene.GetRootGameObjects() : new GameObject[0])
                .Where(go => !selected.Contains(go))
                .Select(go => go.name));

            int renamed = 0;

            // Sozinho vem primeiro e reserva o nome base, se um irmão de fora da seleção já não tiver ele
            foreach (var sameBase in groups.OrderBy(g => g.Count()))
            {
                var objects = sameBase.ToList();
                string format = objects.Count >= 100 ? "000" : "00";
                int number = 0;

                foreach (var go in objects)
                {
                    string newName = sameBase.Key;

                    // Sozinho e sem "(n)" fica como está, a ferramenta só mexe em quem tem sufixo
                    if (objects.Count == 1 && go.name == newName)
                    {
                        taken.Add(newName);
                        continue;
                    }

                    if (objects.Count > 1 || !taken.Add(newName))
                    {
                        do newName = $"{sameBase.Key}_{(++number).ToString(format)}";
                        while (!taken.Add(newName));
                    }

                    if (go.name == newName) continue;
                    Undo.RecordObject(go, "Renomear objeto");
                    go.name = newName;
                    renamed++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Hierarchy Organizer] {renamed} objeto(s) renomeado(s).");
        }

        private static bool DrawColoredGroupButton(string cleanName, string text, string tooltip)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Height(22));

            var background = HierarchyHeaderRenderer.HeaderColor(cleanName);
            var textColor = HierarchyHeaderRenderer.TextColorFor(background);
            if (!GUI.enabled)
            {
                background = Color.Lerp(background, Color.gray, 0.6f);
                textColor = Color.Lerp(textColor, Color.gray, 0.6f);
            }

            EditorGUI.DrawRect(rect, background);

            // Um estilo só, senão cria um GUIStyle por grupo a cada repaint
            GroupButtonStyle.normal.textColor = textColor;
            GUI.Label(rect, text, GroupButtonStyle);

            // Tooltip no label não pega o hover
            return GUI.Button(rect, new GUIContent(" ", tooltip), GUIStyle.none);
        }

        private static void MoveSelectionToGroup(GameObject group)
        {
            Undo.SetCurrentGroupName("Mover Seleção pro Grupo");
            int undoGroup = Undo.GetCurrentGroup();

            int moved = 0;
            foreach (var go in SelectedSceneObjects(true))
            {
                if (go == group || go.name.StartsWith(HeaderPrefix)) continue;
                if (go.transform.parent == group.transform) continue;

                Undo.SetTransformParent(go.transform, group.transform, "Mover pro grupo");
                moved++;
            }

            if (moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(group.scene);
                Debug.Log($"[Hierarchy Organizer] {moved} objeto(s) movido(s) pro grupo '{CleanName(group.name)}'.");
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    [InitializeOnLoad]
    internal static class HierarchyHeaderRenderer
    {
        private const float HierarchyColorStrength = 0.5f;

        private static Color HierarchyHeaderColor(string cleanName)
        {
            var panel = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            return Color.Lerp(panel, HeaderColor(cleanName), HierarchyColorStrength);
        }

        private static GUIStyle _headerStyle;

        private class HeaderLook
        {
            public string CleanName;
            public Color Background;
            public Color Text;
            public string Tooltip;
        }

        // Por nome do grupo, o Hierarchy repinta tudo por evento e isso montava strings por linha
        private static readonly Dictionary<string, HeaderLook> Looks = new Dictionary<string, HeaderLook>();
        private static bool _looksForProSkin;

        private static HeaderLook LookFor(string rawName)
        {
            if (_looksForProSkin != EditorGUIUtility.isProSkin)
            {
                Looks.Clear();
                _looksForProSkin = EditorGUIUtility.isProSkin;
            }

            if (Looks.TryGetValue(rawName, out var look)) return look;

            var cleanName = HierarchyOrganizerTool.CleanName(rawName);
            var background = HierarchyHeaderColor(cleanName);
            look = new HeaderLook
            {
                CleanName = cleanName,
                Background = background,
                Text = TextColorFor(background),
                Tooltip = HierarchyOrganizerTool.TooltipFor(cleanName),
            };
            Looks[rawName] = look;
            return look;
        }

        // Reaproveitados a cada linha, o Hierarchy repinta tudo por evento
        private static readonly GUIContent HeaderLabel = new GUIContent();
        private static readonly GUIContent GroupTip = new GUIContent(" ");
        private static readonly GUIContent ChildrenButtonTip = new GUIContent(" ");
        private static Texture _childrenIcon;
        private static Texture ChildrenIcon =>
            _childrenIcon ??= EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image;

        private static bool HeadersOn =>
            IsaDo3DToolState.IsOn(ToolIds.HierarchyOrganizer) && IsaDo3DToolState.IsOn(ToolIds.HierarchyHeaders);

        // Na 6.4 virou EntityId e na 6.5 o antigo não compila
        static HierarchyHeaderRenderer()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
#if UNITY_6000_5_OR_NEWER
            // A Hierarchy nova (UI Toolkit) não chama o callback antigo
            UHE.HierarchyWindow.BindViewItem += OnBindViewItem;
            UHE.HierarchyWindow.UnbindViewItem += OnUnbindViewItem;
            Selection.selectionChanged += RepaintHeaderRows;
#endif
        }

#if UNITY_6000_4_OR_NEWER
        private static void OnHierarchyItemGUI(EntityId entityId, Rect rect)
        {
            DrawHeader(EditorUtility.EntityIdToObject(entityId) as GameObject, rect);
        }
#else
        private static void OnHierarchyItemGUI(int instanceID, Rect rect)
        {
            DrawHeader(EditorUtility.InstanceIDToObject(instanceID) as GameObject, rect);
        }
#endif

        private static void DrawHeader(GameObject go, Rect rect)
        {
            if (go == null || !HeadersOn) return;
            if (!go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix)) return;

            var look = LookFor(go.name);
            EditorGUI.DrawRect(rect, look.Background);

            _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            _headerStyle.normal.textColor = look.Text;

            // Se o label cobrir o botão os tooltips brigam
            HeaderLabel.text = look.CleanName;
            EditorGUI.LabelField(rect, HeaderLabel, _headerStyle);
            var groupTipRect = new Rect(rect.x, rect.y, rect.width - 30f, rect.height);
            GroupTip.tooltip = look.Tooltip;
            GUI.Label(groupTipRect, GroupTip, EditorStyles.label);

            DrawSelectChildrenButton(rect, go, _headerStyle.normal.textColor);
        }

        private static void DrawSelectChildrenButton(Rect rect, GameObject group, Color tint)
        {
            if (group.transform.childCount == 0) return;

            const float iconSize = 12f;
            var buttonRect = new Rect(
                rect.xMax - 24f,
                rect.y + (rect.height - 16f) * 0.5f,
                19f,
                16f);
            bool hovering = buttonRect.Contains(Event.current.mousePosition);

            EditorGUI.DrawRect(buttonRect, new Color(tint.r, tint.g, tint.b, hovering ? 0.35f : 0.14f));

            var iconRect = new Rect(
                buttonRect.x + (buttonRect.width - iconSize) * 0.5f,
                buttonRect.y + (buttonRect.height - iconSize) * 0.5f,
                iconSize,
                iconSize);

            var prevColor = GUI.color;
            GUI.color = tint;
            if (ChildrenIcon != null)
                GUI.DrawTexture(iconRect, ChildrenIcon, ScaleMode.ScaleToFit);
            else
                GUI.Label(iconRect, "≡");
            GUI.color = prevColor;

            // Sem o " " o tooltip não aparece, e a string só é montada com o mouse em cima
            ChildrenButtonTip.tooltip = hovering ? $"Selecionar os {group.transform.childCount} objeto(s) do grupo" : "";
            if (GUI.Button(buttonRect, ChildrenButtonTip, EditorStyles.label))
                SelectChildren(group);
        }

        private static void SelectChildren(GameObject group)
        {
            var children = new List<Object>();
            foreach (Transform child in group.transform)
                children.Add(child.gameObject);
            Selection.objects = children.ToArray();
        }

#if UNITY_6000_5_OR_NEWER
        private const string HeaderClass = "isado3d-header";
        private const string ChildrenButtonName = "isado3d-select-children";
        private const string ChildrenIconName = "isado3d-select-children-icon";
        private const string CleanNameLabelName = "isado3d-clean-name";

        // A coluna congelada escurece o cabeçalho nas linhas listradas
        private const string FrozenCellClass = "unity-multi-column-view__cell--frozen";

        // O fundo pintado na linha cobre o destaque de hover e seleção da Unity, então ele é refeito aqui
        private static readonly Dictionary<UIE.VisualElement, GameObject> HeaderRows = new Dictionary<UIE.VisualElement, GameObject>();
        private static readonly List<UIE.VisualElement> DeadRows = new List<UIE.VisualElement>();
        private static UIE.VisualElement _hoveredRow;

        // Marca na própria linha, assim sobrevive a sair e voltar do painel sem registrar duas vezes
        private const string HoverWiredClass = "isado3d-hover-wired";

        private static void PaintRow(UIE.VisualElement row, GameObject group)
        {
            var color = LookFor(group.name).Background;
            if (Selection.Contains(group)) color = Color.Lerp(color, Color.black, 0.35f);
            else if (row == _hoveredRow) color = Color.Lerp(color, Color.white, 0.12f);
            row.style.backgroundColor = color;
        }

        // Linha sem painel é janela que fechou sem unbind
        private static void RepaintHeaderRows()
        {
            DeadRows.Clear();
            foreach (var pair in HeaderRows)
            {
                if (pair.Key.panel == null)
                {
                    DeadRows.Add(pair.Key);
                    continue;
                }

                if (pair.Value != null) PaintRow(pair.Key, pair.Value);
            }

            foreach (var row in DeadRows)
                HeaderRows.Remove(row);
        }

        private static void WireHover(UIE.VisualElement row)
        {
            if (row.ClassListContains(HoverWiredClass)) return;
            row.AddToClassList(HoverWiredClass);

            row.RegisterCallback<UIE.PointerEnterEvent>(_ =>
            {
                _hoveredRow = row;
                if (HeaderRows.TryGetValue(row, out var group) && group != null) PaintRow(row, group);
            });
            row.RegisterCallback<UIE.PointerLeaveEvent>(_ =>
            {
                if (_hoveredRow == row) _hoveredRow = null;
                if (HeaderRows.TryGetValue(row, out var group) && group != null) PaintRow(row, group);
            });
            row.RegisterCallback<UIE.DetachFromPanelEvent>(_ =>
            {
                if (_hoveredRow == row) _hoveredRow = null;
            });
        }

        private static void OnUnbindViewItem(UHE.HierarchyWindow window, UH.HierarchyView view, UH.HierarchyViewItem item)
        {
            HeaderRows.Remove(item.RowContainer);
        }

        private static void SetFrozenCellsTransparent(UIE.VisualElement row, bool transparent)
        {
            foreach (var cell in row.Children())
            {
                if (!cell.ClassListContains(FrozenCellClass)) continue;

                if (transparent) cell.style.backgroundColor = Color.clear;
                else cell.style.backgroundColor = UIE.StyleKeyword.Null;
            }
        }

        // Refeito a cada bind porque a linha é reaproveitada
        private class GroupButtonState
        {
            public GameObject Group;
            public UH.HierarchyView View;
            public UH.HierarchyNode Node;
        }

        private static bool AreChildrenSelected(GameObject group)
        {
            int count = group.transform.childCount;
            var selected = Selection.objects;
            if (count == 0 || selected.Length != count) return false;

            foreach (var obj in selected)
                if (!(obj is GameObject go) || go.transform.parent != group.transform) return false;

            return true;
        }

        // Ao fechar seleciona o grupo, senão os filhos escondidos continuam selecionados
        private static void OnGroupButtonClicked(GroupButtonState state)
        {
            if (state == null || state.Group == null) return;

            var node = state.Node;
            if (state.View.IsExpanded(node) && AreChildrenSelected(state.Group))
            {
                state.View.Collapse(node);
                Selection.activeGameObject = state.Group;
                return;
            }

            SelectChildren(state.Group);
            state.View.Expand(node);
        }

        private static bool IsRenameField(UIE.IEventHandler target)
        {
            return target is UIE.VisualElement element
                && (element is UIE.TextField || element.GetFirstAncestorOfType<UIE.TextField>() != null);
        }

        // As linhas são recicladas, todo bind pinta ou desfaz
        private static void OnBindViewItem(UHE.HierarchyWindow window, UH.HierarchyView view, UH.HierarchyViewItem item)
        {
            GameObject go = null;
            var node = item.Node;
            if (node != UH.HierarchyNode.Null && item.Handler is UHE.HierarchyGameObjectHandler handler)
                go = handler.GetGameObject(node);

            var row = item.RowContainer;
            var button = UIE.UQueryExtensions.Q<UIE.Button>(item.RightCustomContainer, ChildrenButtonName);
            // No pai do Name, a Hierarchy desloca esse contêiner no resize e a célula não acompanha
            var nameHost = item.Name.parent;
            if (nameHost == null) return;

            var cleanLabel = UIE.UQueryExtensions.Q<UIE.Label>(nameHost, CleanNameLabelName);

            if (go == null || !HeadersOn || !go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix))
            {
                // Reseta sempre, o Name e o botão podem herdar o estilo de cabeçalho mesmo sem a classe no row
                HeaderRows.Remove(row);
                row.RemoveFromClassList(HeaderClass);
                row.style.backgroundColor = UIE.StyleKeyword.Null;
                SetFrozenCellsTransparent(row, false);
                item.Name.style.visibility = UIE.StyleKeyword.Null;
                nameHost.tooltip = null;
                if (cleanLabel != null) cleanLabel.style.display = UIE.DisplayStyle.None;
                item.Icon.style.display = UIE.StyleKeyword.Null;
                item.OverlayIcon.style.display = UIE.StyleKeyword.Null;
                if (button != null) button.style.display = UIE.DisplayStyle.None;
                return;
            }

            var look = LookFor(go.name);
            var cleanName = look.CleanName;
            var text = look.Text;

            row.AddToClassList(HeaderClass);
            HeaderRows[row] = go;
            WireHover(row);
            PaintRow(row, go);
            SetFrozenCellsTransparent(row, true);
            item.Icon.style.display = UIE.DisplayStyle.None;
            item.OverlayIcon.style.display = UIE.DisplayStyle.None;

            // Não dá pra trocar o texto do Name, o renomear grava o que estiver nele e o grupo perde os ---
            if (cleanLabel == null)
            {
                cleanLabel = new UIE.Label { name = CleanNameLabelName, pickingMode = UIE.PickingMode.Ignore };
                cleanLabel.style.position = UIE.Position.Absolute;
                cleanLabel.style.left = 0f;
                cleanLabel.style.right = 0f;
                cleanLabel.style.top = 0f;
                cleanLabel.style.bottom = 0f;
                cleanLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                cleanLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameHost.Add(cleanLabel);

                // Renomeando mostra o nome de verdade. Só no campo de texto, clicar na linha também dá foco aqui
                var created = cleanLabel;
                nameHost.RegisterCallback<UIE.FocusInEvent>(e =>
                {
                    if (IsRenameField(e.target)) created.style.visibility = UIE.Visibility.Hidden;
                });
                nameHost.RegisterCallback<UIE.FocusOutEvent>(e =>
                {
                    if (IsRenameField(e.target)) created.style.visibility = UIE.StyleKeyword.Null;
                });
            }

            cleanLabel.text = cleanName;
            cleanLabel.style.color = text;
            cleanLabel.style.display = UIE.DisplayStyle.Flex;
            item.Name.style.visibility = UIE.Visibility.Hidden;
            nameHost.tooltip = look.Tooltip;

            if (button == null)
            {
                button = new UIE.Button { name = ChildrenButtonName };
                button.style.width = 19f;
                button.style.height = 16f;
                button.style.marginRight = 4f;
                button.style.paddingLeft = 0f;
                button.style.paddingRight = 0f;
                button.style.borderTopWidth = 0f;
                button.style.borderBottomWidth = 0f;
                button.style.borderLeftWidth = 0f;
                button.style.borderRightWidth = 0f;
                button.style.alignItems = UIE.Align.Center;
                button.style.justifyContent = UIE.Justify.Center;

                var icon = EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image;
                if (icon != null)
                {
                    var image = new UIE.Image { name = ChildrenIconName, image = icon, pickingMode = UIE.PickingMode.Ignore };
                    image.style.width = 12f;
                    image.style.height = 12f;
                    button.Add(image);
                }
                else
                {
                    button.text = "≡";
                }

                var created = button;

                // Senão a Hierarchy entende como renomear o grupo
                created.RegisterCallback<UIE.PointerDownEvent>(e => e.StopPropagation());
                created.RegisterCallback<UIE.PointerUpEvent>(e => e.StopPropagation());
                created.RegisterCallback<UIE.MouseDownEvent>(e => e.StopPropagation());
                created.RegisterCallback<UIE.MouseUpEvent>(e => e.StopPropagation());
                created.RegisterCallback<UIE.ClickEvent>(e => e.StopPropagation());

                created.clicked += () => OnGroupButtonClicked(created.userData as GroupButtonState);
                item.RightCustomContainer.Add(created);
            }

            button.userData = new GroupButtonState { Group = go, View = view, Node = node };
            button.tooltip = $"Selecionar os {go.transform.childCount} objeto(s) do grupo";
            button.style.color = text;
            var childrenIcon = UIE.UQueryExtensions.Q<UIE.Image>(button, ChildrenIconName);
            if (childrenIcon != null) childrenIcon.tintColor = text;
            button.style.backgroundColor = new Color(text.r, text.g, text.b, 0.14f);
            button.style.display = go.transform.childCount > 0 ? UIE.DisplayStyle.Flex : UIE.DisplayStyle.None;
        }
#endif

        internal static Color HeaderColor(string cleanName)
        {
            var key = cleanName.ToUpperInvariant();

            if (HierarchyOrganizerTool.TryGetDefaultColor(key, out var color))
                return Color.Lerp(color, Color.white, 0.3f);

            float hue = StableHash(key) % 100 / 100f;
            return EditorGUIUtility.isProSkin
                ? Color.HSVToRGB(hue, 0.60f, 0.45f)
                : Color.HSVToRGB(hue, 0.30f, 0.92f);
        }

        internal static Color TextColorFor(Color background)
        {
            Color.RGBToHSV(background, out float h, out float s, out float v);
            return background.grayscale > 0.5f
                ? Color.HSVToRGB(h, Mathf.Min(1f, s * 1.15f), 0.20f)
                : Color.HSVToRGB(h, s * 0.30f, 0.97f);
        }

        // string.GetHashCode muda entre sessões
        private static uint StableHash(string s)
        {
            uint hash = 2166136261;
            foreach (char c in s)
                hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
