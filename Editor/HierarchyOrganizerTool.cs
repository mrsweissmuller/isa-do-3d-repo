#if ISADO3D_HIERARCHY_ORGANIZER
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ISADO3D_HIERARCHY_HEADERS && UNITY_6000_5_OR_NEWER
using UH = Unity.Hierarchy;
using UHE = Unity.Hierarchy.Editor;
using UIE = UnityEngine.UIElements;
#endif

namespace IsaDo3D.EnvironmentArt
{
    public class HierarchyOrganizerTool : EditorWindow
    {
        public const string HeaderPrefix = "---";

        private static readonly string[] DefaultGroups =
            { "CORE", "LIGHTING", "VFX", "AUDIO", "UI", "GAMEPLAY", "ENVIRONMENT", "PROPS" };

        private static readonly string[] GameplaySubGroups = { "Spawns", "Interactables", "Triggers" };

        private static readonly Dictionary<string, string> GroupTooltips = new Dictionary<string, string>
        {
            { "CORE", "Câmeras, managers e sistemas centrais da cena." },
            { "LIGHTING", "Luzes, reflection probes, light probes e volumes de iluminação." },
            { "VFX", "Sistemas de partículas e efeitos visuais." },
            { "AUDIO", "Fontes de áudio, ambiências e música." },
            { "UI", "Canvases e elementos de interface." },
            { "GAMEPLAY", "Objetos de level design: spawns, interativos (chests, shrines, breakables) e triggers." },
            { "ENVIRONMENT", "Cenário estático: terreno, arquitetura e meshes marcados como Static." },
            { "PROPS", "Objetos dinâmicos ou interativos do set dressing." },
        };

        public static string TooltipFor(string cleanName)
        {
            return GroupTooltips.TryGetValue(cleanName.ToUpperInvariant(), out var tip)
                ? tip
                : "Grupo personalizado.";
        }

        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle SectionTitleStyle =>
            _sectionTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        private static GUIStyle _groupButtonStyle;
        private static GUIStyle GroupButtonStyle =>
            _groupButtonStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

        private Vector2 _scroll;
        private bool _simulateOnly;

        [MenuItem("Isa do 3D/Cena/Hierarchy Organizer")]
        public static void Open()
        {
            GetWindow<HierarchyOrganizerTool>("Hierarchy Organizer");
        }

        private void OnSelectionChange() => Repaint();
        private void OnHierarchyChange() => Repaint();

        private void OnGUI()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Grupos padrão", SectionTitleStyle);
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
                    EditorGUILayout.LabelField("Mover seleção pra um grupo", SectionTitleStyle);
                    EditorGUILayout.LabelField(
                        $"Objetos selecionados: {Selection.gameObjects.Length}",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    var groups = FindHeaderGroups();
                    if (groups.Count == 0)
                    {
                        EditorGUILayout.HelpBox("Nenhum grupo na cena ainda. Crie os grupos padrão acima.", MessageType.Info);
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
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
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Organizar automaticamente", SectionTitleStyle);
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
                    EditorGUILayout.LabelField("Renomear em lote", SectionTitleStyle);
                    EditorGUILayout.LabelField(
                        "Limpa os sufixos \"(n)\" que a Unity cria ao duplicar e renumera os objetos selecionados. " +
                        "Números que fazem parte do nome do asset (exemplo: Isa_Suja_02) são preservados.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                    {
                        if (GUILayout.Button($"Normalizar nomes da seleção ({Selection.gameObjects.Length})"))
                            NormalizeSelectedNames();
                    }
                }
            }
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

            int created = 0;
            foreach (var name in DefaultGroups)
            {
                var key = name.ToUpperInvariant();
                if (cache.ContainsKey(key)) continue;

                var group = new GameObject($"{HeaderPrefix} {name} {HeaderPrefix}");
                Undo.RegisterCreatedObjectUndo(group, "Criar grupo");
                cache[key] = group;
                created++;
            }

            // Undo.SetSiblingIndex pra reordenação entrar no Ctrl+Z
            int index = 0;
            foreach (var name in DefaultGroups)
            {
                if (!cache.TryGetValue(name.ToUpperInvariant(), out var group)) continue;
#if UNITY_2022_3_OR_NEWER
                Undo.SetSiblingIndex(group.transform, index, "Reordenar grupo");
#else
                // Não existe Undo.SetSiblingIndex na 2021.3
                Undo.RecordObject(group.transform, "Reordenar grupo");
                group.transform.SetSiblingIndex(index);
#endif
                index++;
            }

            foreach (var sub in GameplaySubGroups)
                GetOrCreateGroupPath("GAMEPLAY/" + sub, cache);

            if (created > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log($"[Hierarchy Organizer] {created} grupo(s) criado(s) na cena.");
            }
            else
            {
                Debug.Log("[Hierarchy Organizer] Grupos padrão já existiam, só reordenei.");
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

        // Mesh antes de luz: tocha (mesh + light) é prop
        private static string ClassifyObject(GameObject go)
        {
            var lowerName = go.name.ToLowerInvariant();
            foreach (var (keyword, destination) in NameRules)
                if (lowerName.Contains(keyword)) return destination;

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

        private static GameObject GetOrCreateGroupPath(string path, Dictionary<string, GameObject> cache)
        {
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

                report.AppendLine($"  {go.name} → {groupName}");
                if (!_simulateOnly)
                    Undo.SetTransformParent(go.transform, GetOrCreateGroupPath(groupName, cache).transform, "Mover pro grupo");
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
            var selection = Selection.gameObjects
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
            taken.UnionWith(groups.Where(g => g.Count() == 1).Select(g => g.Key));

            int renamed = 0;
            foreach (var sameBase in groups)
            {
                var objects = sameBase.ToList();
                string format = objects.Count >= 100 ? "000" : "00";
                int number = 0;

                for (int i = 0; i < objects.Count; i++)
                {
                    string newName = sameBase.Key;
                    if (objects.Count > 1)
                    {
                        do newName = $"{sameBase.Key}_{(++number).ToString(format)}";
                        while (!taken.Add(newName));
                    }

                    if (objects[i].name == newName) continue;
                    Undo.RecordObject(objects[i], "Renomear objeto");
                    objects[i].name = newName;
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
            foreach (var go in Selection.gameObjects)
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

    // Existe sempre porque os botões da janela usam essas cores
#if ISADO3D_HIERARCHY_HEADERS
    [InitializeOnLoad]
#endif
    internal static class HierarchyHeaderRenderer
    {
        private static readonly Dictionary<string, Color> GroupColors = new Dictionary<string, Color>
        {
            { "CORE", FromHex(0xe50000) },       //Vermelho
            { "LIGHTING", FromHex(0xff8d00) },   //Laranja
            { "VFX", FromHex(0xffee00) },        //Amarelo
            { "AUDIO", FromHex(0x00811f) },      //Verde
            { "UI", FromHex(0x004cff) },         //Azul
            { "GAMEPLAY", FromHex(0x001a98) },   //Azul-marinho
            { "ENVIRONMENT", FromHex(0x290d7a) }, //Índigo
            { "PROPS", FromHex(0x800080) },      //Roxo
        };

        private static Color FromHex(int hex)
        {
            return new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);
        }

#if ISADO3D_HIERARCHY_HEADERS
        private const float HierarchyColorStrength = 0.5f;

        private static Color HierarchyHeaderColor(string cleanName)
        {
            var panel = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            return Color.Lerp(panel, HeaderColor(cleanName), HierarchyColorStrength);
        }

        private static GUIStyle _headerStyle;

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
            if (go == null) return;
            if (!go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix)) return;

            var cleanName = HierarchyOrganizerTool.CleanName(go.name);
            var background = HierarchyHeaderColor(cleanName);
            EditorGUI.DrawRect(rect, background);

            _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            _headerStyle.normal.textColor = TextColorFor(background);

            // Se o label cobrir o botão os tooltips brigam
            EditorGUI.LabelField(rect, new GUIContent(cleanName), _headerStyle);
            var groupTipRect = new Rect(rect.x, rect.y, rect.width - 30f, rect.height);
            GUI.Label(groupTipRect, new GUIContent(" ", HierarchyOrganizerTool.TooltipFor(cleanName)), EditorStyles.label);

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

            var icon = EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow");
            var tooltip = $"Selecionar os {group.transform.childCount} objeto(s) do grupo";

            var prevColor = GUI.color;
            GUI.color = tint;
            if (icon.image != null)
                GUI.DrawTexture(iconRect, icon.image, ScaleMode.ScaleToFit);
            else
                GUI.Label(iconRect, "≡");
            GUI.color = prevColor;

            // Sem o " " o tooltip não aparece
            if (GUI.Button(buttonRect, new GUIContent(" ", tooltip), EditorStyles.label))
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

        // A coluna congelada escurece o cabeçalho nas linhas listradas
        private const string FrozenCellClass = "unity-multi-column-view__cell--frozen";

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

        // As linhas são recicladas, todo bind pinta ou desfaz
        private static void OnBindViewItem(UHE.HierarchyWindow window, UH.HierarchyView view, UH.HierarchyViewItem item)
        {
            GameObject go = null;
            var node = item.Node;
            if (node != UH.HierarchyNode.Null && item.Handler is UHE.HierarchyGameObjectHandler handler)
                go = handler.GetGameObject(node);

            var row = item.RowContainer;
            var button = UIE.UQueryExtensions.Q<UIE.Button>(item.RightCustomContainer, ChildrenButtonName);

            if (go == null || !go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix))
            {
                // Reseta sempre, o Name e o botão podem herdar o estilo de cabeçalho mesmo sem a classe no row
                row.RemoveFromClassList(HeaderClass);
                row.style.backgroundColor = UIE.StyleKeyword.Null;
                SetFrozenCellsTransparent(row, false);
                item.Name.style.color = UIE.StyleKeyword.Null;
                item.Name.style.unityFontStyleAndWeight = UIE.StyleKeyword.Null;
                item.Name.tooltip = null;
                if (button != null) button.style.display = UIE.DisplayStyle.None;
                return;
            }

            var cleanName = HierarchyOrganizerTool.CleanName(go.name);
            var background = HierarchyHeaderColor(cleanName);
            var text = TextColorFor(background);

            row.AddToClassList(HeaderClass);
            row.style.backgroundColor = background;
            SetFrozenCellsTransparent(row, true);
            item.Name.style.color = text;
            item.Name.style.unityFontStyleAndWeight = FontStyle.Bold;
            item.Name.tooltip = HierarchyOrganizerTool.TooltipFor(cleanName);

            if (button == null)
            {
                button = new UIE.Button { name = ChildrenButtonName, text = "≡" };
                button.style.width = 19f;
                button.style.height = 16f;
                button.style.marginRight = 4f;
                button.style.paddingLeft = 0f;
                button.style.paddingRight = 0f;
                button.style.borderTopWidth = 0f;
                button.style.borderBottomWidth = 0f;
                button.style.borderLeftWidth = 0f;
                button.style.borderRightWidth = 0f;
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
            button.style.backgroundColor = new Color(text.r, text.g, text.b, 0.14f);
            button.style.display = go.transform.childCount > 0 ? UIE.DisplayStyle.Flex : UIE.DisplayStyle.None;
        }
#endif
#endif //ISADO3D_HIERARCHY_HEADERS

        internal static Color HeaderColor(string cleanName)
        {
            var key = cleanName.ToUpperInvariant();

            if (GroupColors.TryGetValue(key, out var color))
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
#endif //ISADO3D_HIERARCHY_ORGANIZER
