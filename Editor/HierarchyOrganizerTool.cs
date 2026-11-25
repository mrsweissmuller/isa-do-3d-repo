using System.Collections.Generic;
using System.IO;
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
    public partial class HierarchyOrganizerTool : EditorWindow
    {
        public const string HeaderPrefix = "---";

        // Título sem cor, só o nome entre duas linhas, pra separar blocos de grupos
        public const string TitlePrefix = "===";

        public static bool IsTitle(string name)
        {
            return name.StartsWith(TitlePrefix);
        }

        public static string CleanTitle(string titleName)
        {
            return titleName.Trim('=', ' ');
        }

        private class DefaultGroup
        {
            public string Name;
            public Color Color;
            public string Tooltip;
            // Nomes antigos que continuam valendo pro mesmo grupo
            public string[] Aliases = System.Array.Empty<string>();
        }

        // Na ordem em que entram na cena. As seis primeiras cores seguem a ordem dos banners da referência
        private static readonly DefaultGroup[] DefaultGroups =
        {
            new DefaultGroup
            {
                Name = "SETUP", Color = FromHex(0x3d64c7), // Azul
                Tooltip = "Cameras, managers and core scene systems.",
                Aliases = new[] { "CORE" },
            },
            new DefaultGroup
            {
                Name = "LIGHTING", Color = FromHex(0x4b8f27), // Verde
                Tooltip = "Lights, reflection probes, light probes and lighting volumes.",
            },
            new DefaultGroup
            {
                Name = "VFX", Color = FromHex(0xbb7723), // Laranja
                Tooltip = "Particle systems and visual effects.",
            },
            new DefaultGroup
            {
                Name = "AUDIO", Color = FromHex(0x7137a5), // Roxo
                Tooltip = "Audio sources, ambiences and music.",
            },
            new DefaultGroup
            {
                Name = "UI", Color = FromHex(0xb85b88), // Rosa
                Tooltip = "Canvases and interface elements.",
            },
            new DefaultGroup
            {
                Name = "GAMEPLAY", Color = FromHex(0x539ba5), // Turquesa
                Tooltip = "Level design objects: spawns, interactables and triggers.",
            },
            new DefaultGroup
            {
                Name = "ENVIRONMENT", Color = FromHex(0xb74843), // Coral, fora da referência
                Tooltip = "Static scenery: terrain, architecture and static meshes.",
            },
            new DefaultGroup
            {
                Name = "PROPS", Color = FromHex(0xb1992f), // Dourado, fora da referência
                Tooltip = "Dynamic or interactive set dressing.",
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
            return DefaultGroups.FirstOrDefault(g => g.Name == key || g.Aliases.Contains(key));
        }

        // Grupo antigo (CORE) conta como o padrão atual (SETUP), senão criava um duplicado
        internal static string CanonicalGroupKey(string cleanName)
        {
            return FindDefaultGroup(cleanName)?.Name ?? cleanName.ToUpperInvariant();
        }

        public static string TooltipFor(string cleanName)
        {
            return FindDefaultGroup(cleanName)?.Tooltip ?? "Custom group.";
        }

        internal static bool TryGetDefaultColor(string cleanName, out Color color)
        {
            var group = FindDefaultGroup(cleanName);
            color = group?.Color ?? default;
            return group != null;
        }

        [MenuItem("Art Tools/Hierarchy Organizer", true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.HierarchyOrganizer);

        [MenuItem("Art Tools/Hierarchy Organizer", false, 2)]
        public static void Open()
        {
            GetWindow<HierarchyOrganizerTool>("Hierarchy Organizer");
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
                .GroupBy(g => CanonicalGroupKey(CleanName(g.name)))
                .ToDictionary(gr => gr.Key, gr => gr.First());
        }

        private static void CreateDefaultGroups()
        {
            Undo.SetCurrentGroupName("Create Default Groups");
            int undoGroup = Undo.GetCurrentGroup();

            var cache = BuildGroupCache();

            int created = 0, reordered = 0;
            var ordered = new List<GameObject>();
            var createdNow = new HashSet<GameObject>();
            foreach (var group in DefaultGroups)
            {
                int before = cache.Count;
                var go = GetOrCreateGroup(group.Name, cache);
                if (cache.Count > before)
                {
                    created++;
                    createdNow.Add(go);
                }
                ordered.Add(go);
            }

            // Com separador na cena cada grupo fica na vaga de um grupo, senão os separadores iam todos pro fim
            bool keepSlots = SceneManager.GetActiveScene().GetRootGameObjects().Any(go => IsTitle(go.name));
            var slots = keepSlots
                ? ordered.Select(go => go.transform.GetSiblingIndex()).OrderBy(i => i).ToList()
                : Enumerable.Range(0, ordered.Count).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                // Grupo recém-criado vai pro lugar dele sem contar como reordenado
                if (SetSiblingIndexWithUndo(ordered[i].transform, slots[i]) && !createdNow.Contains(ordered[i]))
                    reordered++;
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
                Debug.Log($"[Hierarchy Organizer] Created {created} group(s) and {subCreated} subgroup(s), reordered {reordered}.");
            }
            else
            {
                Debug.Log("[Hierarchy Organizer] Default groups already exist and are in order.");
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private static bool SetSiblingIndexWithUndo(Transform transform, int index)
        {
            if (transform.GetSiblingIndex() == index) return false;

#if UNITY_2022_3_OR_NEWER
            // Undo.SetSiblingIndex pra reordenação entrar no Ctrl+Z
            Undo.SetSiblingIndex(transform, index, "Reorder group");
#else
            // Não existe Undo.SetSiblingIndex na 2021.3
            Undo.RecordObject(transform, "Reorder group");
            transform.SetSiblingIndex(index);
#endif
            return true;
        }

        private static List<string> MissingDefaultGroups()
        {
            var cache = BuildGroupCache();
            return DefaultGroups.Where(g => !cache.ContainsKey(g.Name)).Select(g => g.Name).ToList();
        }

        // Só os padrão que existem na cena, na ordem em que deveriam estar
        private static List<GameObject> PresentDefaultGroups()
        {
            var cache = BuildGroupCache();
            return DefaultGroups.Where(g => cache.ContainsKey(g.Name)).Select(g => cache[g.Name]).ToList();
        }

        private static bool InSiblingOrder(List<GameObject> groups)
        {
            for (int i = 1; i < groups.Count; i++)
                if (groups[i].transform.GetSiblingIndex() < groups[i - 1].transform.GetSiblingIndex()) return false;
            return true;
        }

        // Troca os grupos entre as vagas que eles já ocupam, o resto da raiz não sai do lugar
        private static void ReorderDefaultGroups()
        {
            var present = PresentDefaultGroups();
            if (present.Count == 0) return;

            Undo.SetCurrentGroupName("Reorder Default Groups");
            int undoGroup = Undo.GetCurrentGroup();

            var slots = present.Select(go => go.transform.GetSiblingIndex()).OrderBy(i => i).ToList();
            for (int i = 0; i < present.Count; i++)
                SetSiblingIndexWithUndo(present[i].transform, slots[i]);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Nome ganha de componente (chest tem mesh, mas é gameplay)
        private static readonly (string Keyword, string Destination)[] NameRules =
        {
            ("spawn", "GAMEPLAY/Spawns"),
            ("spawner", "GAMEPLAY/Spawns"),
            ("spawnpoint", "GAMEPLAY/Spawns"),
            ("patrol", "GAMEPLAY/Spawns"),
            ("trigger", "GAMEPLAY/Triggers"),
            ("checkpoint", "GAMEPLAY/Triggers"),
            ("chest", "GAMEPLAY/Interactables"),
            ("shrine", "GAMEPLAY/Interactables"),
            ("breakable", "GAMEPLAY/Interactables"),
            ("door", "GAMEPLAY/Interactables"),
            ("lever", "GAMEPLAY/Interactables"),
            ("interactable", "GAMEPLAY/Interactables"),
            ("interactive", "GAMEPLAY/Interactables"),
        };

        // Por palavra inteira, senão Outdoor_Lamp e Doorway viram porta
        private static string[] NameTokens(string name)
        {
            var spaced = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant();
            return Regex.Split(spaced, "[^a-z0-9]+");
        }

        // Mesh antes de luz e de partícula: tocha (mesh + light + chama) é prop
        // Por tipo, ler o texto do script a cada objeto ficaria caro
        private static readonly Dictionary<System.Type, bool> RootOnlyTypes = new Dictionary<System.Type, bool>();
        private static Dictionary<string, MonoScript> _scriptsByName;

        private static bool ScriptNeedsRoot(System.Type type)
        {
            if (RootOnlyTypes.TryGetValue(type, out bool known)) return known;

            if (_scriptsByName == null)
            {
                _scriptsByName = new Dictionary<string, MonoScript>();
                foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
                    if (script != null) _scriptsByName[script.name] = script;
            }

            bool needsRoot = false;

            // A classe base conta, singleton genérico costuma chamar o DontDestroyOnLoad por ela
            for (var current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                string name = current.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);

                if (_scriptsByName.TryGetValue(name, out var script) && script.text.Contains("DontDestroyOnLoad"))
                {
                    needsRoot = true;
                    break;
                }
            }

            RootOnlyTypes[type] = needsRoot;
            return needsRoot;
        }

        // DontDestroyOnLoad só funciona em objeto da raiz da cena, dentro de um grupo ele para de persistir
        internal static bool NeedsSceneRoot(GameObject go)
        {
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
                if (behaviour != null && ScriptNeedsRoot(behaviour.GetType())) return true;
            return false;
        }

        private static string ClassifyObject(GameObject go)
        {
            if (NeedsSceneRoot(go)) return null;

            var tokens = NameTokens(go.name);
            foreach (var (keyword, destination) in NameRules)
                if (tokens.Any(t => t == keyword || t == keyword + "s")) return destination;

            if (go.GetComponentInChildren<Terrain>(true) != null) return "ENVIRONMENT";
            if (go.GetComponentInChildren<Canvas>(true) != null) return "UI";
            if (go.GetComponentInChildren<Camera>(true) != null) return "SETUP";

            bool hasMesh = go.GetComponentInChildren<MeshRenderer>(true) != null
                        || go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
            if (hasMesh)
            {
                bool isStatic = go.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.isStatic);
                return isStatic ? "ENVIRONMENT" : "PROPS";
            }

            if (go.GetComponentInChildren<ParticleSystem>(true) != null) return "VFX";

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
            Undo.RegisterCreatedObjectUndo(group, "Create group");
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
                    Undo.RegisterCreatedObjectUndo(sub, "Create subgroup");
                    Undo.SetTransformParent(sub.transform, current, "Place subgroup");
                    child = sub.transform;
                    subgroupsCreated++;
                }
                current = child;
            }

            return current.gameObject;
        }

        // Só sugere. Destination nulo é objeto sem regra, e nada se move antes de confirmar na janela
        private static List<(GameObject Object, string Destination)> PlanAutoOrganize()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(go => !go.name.StartsWith(HeaderPrefix) && !IsTitle(go.name))
                .Select(go => (go, ClassifyObject(go)))
                .ToList();
        }

        // Padrões, subgrupos de GAMEPLAY e os grupos personalizados que já estão na cena
        private static List<string> OrganizeDestinations()
        {
            var destinations = new List<string>();
            foreach (var group in DefaultGroups)
            {
                destinations.Add(group.Name);
                if (group.Name != "GAMEPLAY") continue;
                foreach (var sub in GameplaySubGroups) destinations.Add("GAMEPLAY/" + sub);
            }

            foreach (var custom in FindHeaderGroups().Select(g => CleanName(g.name)))
            {
                if (FindDefaultGroup(custom) == null && !destinations.Contains(custom)) destinations.Add(custom);
            }

            return destinations;
        }

        private static int ApplyAutoOrganize(List<(GameObject Object, string Destination)> moves)
        {
            Undo.SetCurrentGroupName("Auto Organize Hierarchy");
            int undoGroup = Undo.GetCurrentGroup();

            var cache = BuildGroupCache();
            var scene = SceneManager.GetActiveScene();

            int moved = 0;
            foreach (var (go, destination) in moves)
            {
                // A prévia pode estar velha: objeto apagado, de outra cena ou que já saiu da raiz
                if (go == null || string.IsNullOrEmpty(destination)) continue;
                if (go.scene != scene || go.transform.parent != null) continue;

                Undo.SetTransformParent(go.transform, GetOrCreateGroupPath(destination, cache, out _).transform, "Move to group");
                moved++;
            }

            if (moved > 0) EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[Hierarchy Organizer] Organized {moved} object(s).");
            return moved;
        }

        // O _02 é do asset, só o " (n)" é da Unity
        private static string BaseName(string name)
        {
            var match = Regex.Match(name.Trim(), @"^(.*?)(?:\s*\(\d+\))*$");
            var baseName = match.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(baseName) ? name.Trim() : baseName;
        }

        // Tira qualquer contador do fim, nos três esquemas de duplicata da Unity
        private static string Stem(string name)
        {
            var stem = Regex.Replace(name.Trim(), @"(?:\s*\(\d+\)|[._][0-9]+)+$", "");
            return string.IsNullOrEmpty(stem) ? name.Trim() : stem;
        }

        // No esquema Prefab_1 a Unity troca o _01 do asset pelo contador, só o prefab de origem sabe o nome certo
        private static string BaseNameOf(GameObject go)
        {
            var fallback = BaseName(go.name);
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return fallback;

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(assetPath)) return fallback;

            // Nome trocado de propósito não volta pro nome do prefab
            var prefabName = Path.GetFileNameWithoutExtension(assetPath);
            return Stem(go.name) == Stem(prefabName) ? prefabName : fallback;
        }

        // Tira os sufixos de duplicata da Unity e renumera a seleção. Quem chama é o botão da aba Organize
        private static void NormalizeSelectedNames()
        {
            var selection = SelectedSceneObjects()
                .Where(go => !go.name.StartsWith(HeaderPrefix) && !IsTitle(go.name))
                .OrderBy(go => go.transform.GetSiblingIndex())
                .ToList();
            if (selection.Count == 0) return;

            Undo.SetCurrentGroupName("Normalize Names");
            int undoGroup = Undo.GetCurrentGroup();

            var groups = selection.GroupBy(BaseNameOf).ToList();

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
                    Undo.RecordObject(go, "Rename object");
                    go.name = newName;
                    renamed++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Hierarchy Organizer] Renamed {renamed} object(s).");
        }

        private static void MoveSelectionToGroup(GameObject group)
        {
            Undo.SetCurrentGroupName("Move Selection to Group");
            int undoGroup = Undo.GetCurrentGroup();

            int moved = 0;
            foreach (var go in SelectedSceneObjects(true))
            {
                if (go == group || go.name.StartsWith(HeaderPrefix) || IsTitle(go.name)) continue;
                if (go.transform.parent == group.transform) continue;

                Undo.SetTransformParent(go.transform, group.transform, "Move to group");
                moved++;
            }

            if (moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(group.scene);
                Debug.Log($"[Hierarchy Organizer] Moved {moved} object(s) to '{CleanName(group.name)}'.");
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    [InitializeOnLoad]
    internal static class HierarchyHeaderRenderer
    {
        // A cor aparece na Hierarchy exatamente como foi definida, sem misturar com o painel
        internal static Color HierarchyHeaderColor(string cleanName)
        {
            return HeaderColor(cleanName);
        }

        private static GUIStyle _headerStyle;

        internal class HeaderLook
        {
            public string CleanName;
            public Color Background;
            public bool HasBackground;
            public Color Text;
            // Sem alinhamento escolhido cada Hierarchy usa o dela
            public TextAnchor? Anchor;
            public FontStyle Font;
            public string Tooltip;
        }

        // Por nome do grupo, o Hierarchy repinta tudo por evento e isso montava strings por linha
        private static readonly Dictionary<string, HeaderLook> Looks = new Dictionary<string, HeaderLook>();
        private static bool _looksForProSkin;

        internal static HeaderLook LookFor(string rawName)
        {
            if (_looksForProSkin != EditorGUIUtility.isProSkin)
            {
                Looks.Clear();
                _looksForProSkin = EditorGUIUtility.isProSkin;
            }

            if (Looks.TryGetValue(rawName, out var look)) return look;

            var cleanName = HierarchyOrganizerTool.CleanName(rawName);
            var style = HierarchyGroupColors.Get(cleanName);
            bool hasBackground = style == null || !style.NoBackground;
            var background = HierarchyHeaderColor(cleanName);
            look = new HeaderLook
            {
                CleanName = cleanName,
                Background = background,
                HasBackground = hasBackground,
                Text = style?.Text ?? (hasBackground ? TextColorFor(background) : TitleColor),
                Anchor = style?.Align,
                Font = style?.Font ?? FontStyle.Bold,
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
            HierarchyGroupColors.Changed += OnGroupColorsChanged;
            HierarchyViewPrefs.Changed += OnGroupColorsChanged;
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
            EditorApplication.hierarchyChanged += RepaintTreeLines;
#endif
        }

        internal static void SetExpanded(List<GameObject> groups, bool expanded)
        {
#if UNITY_6000_5_OR_NEWER
            foreach (var window in Resources.FindObjectsOfTypeAll<UHE.HierarchyWindow>())
            {
                var view = window.View;
                var handler = view?.Source?.GetNodeTypeHandlerBase<UHE.HierarchyGameObjectHandler>();
                if (handler == null) continue;

                foreach (var group in groups)
                {
                    if (group == null) continue;

                    var node = handler.GetOrCreateNode(group);
                    if (expanded) view.Expand(node);
                    else view.Collapse(node);
                }
            }
#else
            // A Hierarchy antiga não tem API pública pra isso
            var windowType = typeof(Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            var setExpanded = windowType?.GetMethod(
                "SetExpanded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(int), typeof(bool) }, null);
            if (setExpanded == null) return;

            foreach (var window in Resources.FindObjectsOfTypeAll(windowType))
            {
                foreach (var group in groups)
                    if (group != null) setExpanded.Invoke(window, new object[] { group.GetInstanceID(), expanded });
            }
#endif
        }

        private static void OnGroupColorsChanged()
        {
            Looks.Clear();
            EditorApplication.RepaintHierarchyWindow();

            // A Hierarchy nova só refaz o bind das linhas assim
            EditorApplication.DirtyHierarchyWindowSorting();
#if UNITY_6000_5_OR_NEWER
            RepaintHeaderRows();
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

        // Cabeçalho mais próximo acima do objeto, em qualquer profundidade
        private static GameObject FindHeaderAncestor(GameObject go)
        {
            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
                if (parent.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix)) return parent.gameObject;
            return null;
        }

        private static Color ChildTint(GameObject group)
        {
            var look = LookFor(group.name);
            if (!look.HasBackground || !HierarchyViewPrefs.ChildTint) return Color.clear;

            var color = look.Background;
            color.a = HierarchyViewPrefs.ChildTintAlpha;
            return color;
        }

        private static void DrawHeader(GameObject go, Rect rect)
        {
            if (go == null || !HeadersOn) return;
            if (HierarchyOrganizerTool.IsTitle(go.name))
            {
                DrawTitle(go, rect);
                return;
            }

            if (!go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix))
            {
                // Selecionado fica com o destaque da Unity
                var owner = FindHeaderAncestor(go);
                if (owner != null && !Selection.Contains(go)) EditorGUI.DrawRect(rect, ChildTint(owner));
                return;
            }

            var look = LookFor(go.name);
            // Sem fundo ainda precisa cobrir o nome de verdade, que tem os ---
            EditorGUI.DrawRect(rect, look.HasBackground ? look.Background : RowCoverColor(go));

            _headerStyle ??= new GUIStyle(EditorStyles.label);
            _headerStyle.alignment = look.Anchor ?? TextAnchor.MiddleCenter;
            _headerStyle.fontStyle = look.Font;
            _headerStyle.normal.textColor = look.Text;

            // Se o label cobrir o botão os tooltips brigam
            HeaderLabel.text = look.CleanName;
            EditorGUI.LabelField(rect, HeaderLabel, _headerStyle);
            var groupTipRect = new Rect(rect.x, rect.y, rect.width - 30f, rect.height);
            GroupTip.tooltip = look.Tooltip;
            GUI.Label(groupTipRect, GroupTip, EditorStyles.label);

            DrawSelectChildrenButton(rect, go, _headerStyle.normal.textColor);
        }

        private static Color TitleColor =>
            EditorGUIUtility.isProSkin ? new Color(0.82f, 0.82f, 0.82f) : new Color(0.15f, 0.15f, 0.15f);

        private static Color RowCoverColor(GameObject go)
        {
            if (Selection.Contains(go))
                return EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);
            return EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
        }

        private static Color SeparatorColor =>
            EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.32f, 0.32f, 0.32f);

        private static GUIStyle _titleStyle;
        private static readonly GUIContent TitleLabel = new GUIContent();

        private static void DrawTitle(GameObject go, Rect rect)
        {
            // De ponta a ponta, o rect da Unity começa só depois do recuo
            rect.xMin = 32f;

            // Cobre o nome de verdade, que tem os ===
            Color cover;
            if (Selection.Contains(go))
                cover = EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);
            else
                cover = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            EditorGUI.DrawRect(rect, cover);

            _titleStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            _titleStyle.normal.textColor = SeparatorColor;

            TitleLabel.text = HierarchyOrganizerTool.CleanTitle(go.name);
            float textWidth = _titleStyle.CalcSize(TitleLabel).x + 12f;
            EditorGUI.LabelField(rect, TitleLabel, _titleStyle);

            float ruleWidth = (rect.width - textWidth) * 0.5f;
            if (ruleWidth <= 0f) return;

            var rule = SeparatorColor;
            rule.a = 0.5f;
            float y = Mathf.Round(rect.y + rect.height * 0.5f);
            EditorGUI.DrawRect(new Rect(rect.x, y, ruleWidth, 1f), rule);
            EditorGUI.DrawRect(new Rect(rect.xMax - ruleWidth, y, ruleWidth, 1f), rule);
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
            ChildrenButtonTip.tooltip = hovering ? $"Select the {group.transform.childCount} object(s) in this group" : "";
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

        // Linhas de dentro de um grupo, com o objeto da linha e o cabeçalho que dá a cor
        private static readonly Dictionary<UIE.VisualElement, (GameObject Object, GameObject Group)> ChildRows =
            new Dictionary<UIE.VisualElement, (GameObject Object, GameObject Group)>();

        private const string TreeLinesName = "isado3d-tree-lines";
        private const string TitleBoxName = "isado3d-title";
        private const string TitleLabelName = "isado3d-title-label";

        private static UIE.VisualElement TitleRule()
        {
            var color = SeparatorColor;
            color.a = 0.5f;

            var rule = new UIE.VisualElement { pickingMode = UIE.PickingMode.Ignore };
            rule.style.flexGrow = 1f;
            rule.style.height = 1f;
            rule.style.backgroundColor = color;
            return rule;
        }

        // Não dá pra trocar o texto do Name, então o título é desenhado por cima dele
        private static void ShowTitle(UH.HierarchyViewItem item, UIE.VisualElement nameHost, UIE.VisualElement box, string rawName)
        {
            if (box == null)
            {
                box = new UIE.VisualElement { name = TitleBoxName, pickingMode = UIE.PickingMode.Ignore };
                box.style.position = UIE.Position.Absolute;
                box.style.left = 6f;
                box.style.right = 6f;
                box.style.top = 0f;
                box.style.bottom = 0f;
                box.style.flexDirection = UIE.FlexDirection.Row;
                box.style.alignItems = UIE.Align.Center;

                var label = new UIE.Label { name = TitleLabelName, pickingMode = UIE.PickingMode.Ignore };
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.fontSize = 10f;
                label.style.color = SeparatorColor;
                label.style.marginLeft = 8f;
                label.style.marginRight = 8f;
                label.style.paddingLeft = 0f;
                label.style.paddingRight = 0f;

                box.Add(TitleRule());
                box.Add(label);
                box.Add(TitleRule());

                // Na linha e não na célula do nome, pra ir de ponta a ponta
                item.RowContainer.Add(box);

                // Renomeando mostra o nome de verdade
                var created = box;
                nameHost.RegisterCallback<UIE.FocusInEvent>(e =>
                {
                    if (IsRenameField(e.target)) created.style.visibility = UIE.Visibility.Hidden;
                });
                nameHost.RegisterCallback<UIE.FocusOutEvent>(e =>
                {
                    if (IsRenameField(e.target)) created.style.visibility = UIE.StyleKeyword.Null;
                });
            }

            UIE.UQueryExtensions.Q<UIE.Label>(box, TitleLabelName).text = HierarchyOrganizerTool.CleanTitle(rawName);
            box.style.display = UIE.DisplayStyle.Flex;
            item.Name.style.visibility = UIE.Visibility.Hidden;
            item.Icon.style.display = UIE.DisplayStyle.None;
            item.OverlayIcon.style.display = UIE.DisplayStyle.None;
        }

        // Sem seta a Unity não informa a largura do recuo, então é o padrão da Hierarchy
        private const float FallbackIndent = 14f;
        private const float ArrowGap = 6f;

        private class TreeLineState
        {
            public GameObject Object;
            public GameObject Group;
            public UIE.VisualElement Toggle;
            public UIE.VisualElement Icon;
        }

        // X da linha de cada nível, medido nas linhas já desenhadas, pra linha do avô passar no lugar certo
        private static readonly Dictionary<int, float> LineXByDepth = new Dictionary<int, float>();

        // Linhas ligando os objetos ao grupo. Na busca a lista fica plana e não tem o que ligar
        private static void UpdateTreeLines(UH.HierarchyViewItem item, UH.HierarchyView view, GameObject go, GameObject group)
        {
            var row = item.RowContainer;
            var lines = UIE.UQueryExtensions.Q<UIE.VisualElement>(row, TreeLinesName);

            if (group == null || view.Filtering || !HierarchyViewPrefs.TreeLines)
            {
                if (lines != null) lines.style.display = UIE.DisplayStyle.None;
                return;
            }

            if (lines == null)
            {
                lines = new UIE.VisualElement { name = TreeLinesName, pickingMode = UIE.PickingMode.Ignore };
                lines.style.position = UIE.Position.Absolute;
                lines.style.left = 0f;
                lines.style.right = 0f;
                lines.style.top = 0f;
                lines.style.bottom = 0f;
                lines.generateVisualContent += DrawTreeLines;
                row.Add(lines);
            }

            lines.userData = new TreeLineState { Object = go, Group = group, Toggle = item.Toggle, Icon = item.Icon };
            lines.style.display = UIE.DisplayStyle.Flex;
            lines.MarkDirtyRepaint();
        }

        // Irmão novo ou reordenado muda quem é o último, e essas linhas não passam por bind de novo
        private static void RepaintTreeLines()
        {
            foreach (var row in ChildRows.Keys)
                UIE.UQueryExtensions.Q<UIE.VisualElement>(row, TreeLinesName)?.MarkDirtyRepaint();
        }

        private static bool IsLastSibling(Transform t)
        {
            return t.parent == null || t.GetSiblingIndex() == t.parent.childCount - 1;
        }

        private static int DepthOf(Transform t)
        {
            int depth = 0;
            for (var parent = t.parent; parent != null; parent = parent.parent) depth++;
            return depth;
        }

        private static float Crisp(float x)
        {
            return Mathf.Round(x) + 0.5f;
        }

        private static void DrawTreeLines(UIE.MeshGenerationContext context)
        {
            var lines = context.visualElement;
            if (!(lines.userData is TreeLineState state) || state.Object == null || state.Group == null) return;

            var iconBounds = state.Icon.worldBound;
            if (float.IsNaN(iconBounds.x) || iconBounds.width <= 0f) return;

            float height = lines.contentRect.height;
            float middle = Crisp(height * 0.5f);
            float iconLeft = UIE.VisualElementExtensions.WorldToLocal(lines, new Vector2(iconBounds.xMin, 0f)).x;

            // A linha desce pelo centro da coluna da seta
            float x = iconLeft - 9f;
            if (state.Toggle != null)
            {
                var toggleBounds = state.Toggle.worldBound;
                if (!float.IsNaN(toggleBounds.x) && toggleBounds.width > 0f)
                    x = UIE.VisualElementExtensions.WorldToLocal(lines, new Vector2(toggleBounds.center.x, 0f)).x;
            }
            x = Crisp(x);

            var transform = state.Object.transform;
            int depth = DepthOf(transform);
            LineXByDepth[depth] = x;

            var groupLook = LookFor(state.Group.name);
            var color = groupLook.HasBackground ? Color.Lerp(groupLook.Background, Color.white, 0.35f) : groupLook.Text;
            color.a = 0.8f;

            var painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = color;
            painter.BeginPath();

            bool last = IsLastSibling(transform);
            if (transform.childCount > 0)
            {
                // Com seta a linha abre espaço pra ela
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, middle - ArrowGap));
                if (!last)
                {
                    painter.MoveTo(new Vector2(x, middle + ArrowGap));
                    painter.LineTo(new Vector2(x, height));
                }
            }
            else
            {
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, last ? middle : height));
                painter.MoveTo(new Vector2(x, middle));
                painter.LineTo(new Vector2(iconLeft - 2f, middle));
            }

            // Níveis de cima que ainda têm irmão embaixo continuam descendo por esta linha
            int level = depth - 1;
            for (var ancestor = transform.parent;
                 ancestor != null && ancestor.gameObject != state.Group;
                 ancestor = ancestor.parent, level--)
            {
                if (IsLastSibling(ancestor)) continue;

                float ancestorX = LineXByDepth.TryGetValue(level, out var known)
                    ? known
                    : x - (depth - level) * FallbackIndent;
                painter.MoveTo(new Vector2(ancestorX, 0f));
                painter.LineTo(new Vector2(ancestorX, height));
            }

            painter.Stroke();
        }

        // Selecionada ou com o mouse em cima volta pro visual da Unity, senão a cor cobria o destaque
        private static void PaintChildRow(UIE.VisualElement row, GameObject go, GameObject group)
        {
            bool native = go == null || group == null || row == _hoveredRow || Selection.Contains(go)
                          || ChildTint(group).a <= 0f;
            if (native) row.style.backgroundColor = UIE.StyleKeyword.Null;
            else row.style.backgroundColor = ChildTint(group);
            SetFrozenCellsTransparent(row, !native);
        }

        private static void PaintRow(UIE.VisualElement row, GameObject group)
        {
            var look = LookFor(group.name);
            if (!look.HasBackground)
            {
                // Sem fundo a linha fica com o visual da Unity
                row.style.backgroundColor = UIE.StyleKeyword.Null;
                SetFrozenCellsTransparent(row, false);
                return;
            }

            var color = look.Background;
            if (Selection.Contains(group)) color = Color.Lerp(color, Color.black, 0.35f);
            else if (row == _hoveredRow) color = Color.Lerp(color, Color.white, 0.12f);
            row.style.backgroundColor = color;
            SetFrozenCellsTransparent(row, true);
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

            DeadRows.Clear();
            foreach (var pair in ChildRows)
            {
                if (pair.Key.panel == null)
                {
                    DeadRows.Add(pair.Key);
                    continue;
                }

                PaintChildRow(pair.Key, pair.Value.Object, pair.Value.Group);
            }

            foreach (var row in DeadRows)
                ChildRows.Remove(row);
        }

        private static void RepaintRow(UIE.VisualElement row)
        {
            if (HeaderRows.TryGetValue(row, out var group))
            {
                if (group != null) PaintRow(row, group);
            }
            else if (ChildRows.TryGetValue(row, out var child))
            {
                PaintChildRow(row, child.Object, child.Group);
            }
        }

        private static void WireHover(UIE.VisualElement row)
        {
            if (row.ClassListContains(HoverWiredClass)) return;
            row.AddToClassList(HoverWiredClass);

            row.RegisterCallback<UIE.PointerEnterEvent>(_ =>
            {
                _hoveredRow = row;
                RepaintRow(row);
            });
            row.RegisterCallback<UIE.PointerLeaveEvent>(_ =>
            {
                if (_hoveredRow == row) _hoveredRow = null;
                RepaintRow(row);
            });
            row.RegisterCallback<UIE.DetachFromPanelEvent>(_ =>
            {
                if (_hoveredRow == row) _hoveredRow = null;
            });
        }

        private static void OnUnbindViewItem(UHE.HierarchyWindow window, UH.HierarchyView view, UH.HierarchyViewItem item)
        {
            HeaderRows.Remove(item.RowContainer);
            ChildRows.Remove(item.RowContainer);
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

            // A linha é reaproveitada, então o título some até o bind dizer que é título
            var titleBox = UIE.UQueryExtensions.Q<UIE.VisualElement>(row, TitleBoxName);
            if (titleBox != null) titleBox.style.display = UIE.DisplayStyle.None;

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

                bool isTitle = go != null && HeadersOn && HierarchyOrganizerTool.IsTitle(go.name);
                if (isTitle) ShowTitle(item, nameHost, titleBox, go.name);

                var owner = go != null && HeadersOn ? FindHeaderAncestor(go) : null;
                UpdateTreeLines(item, view, go, isTitle ? null : owner);
                if (owner == null)
                {
                    ChildRows.Remove(row);
                    return;
                }

                ChildRows[row] = (go, owner);
                WireHover(row);
                PaintChildRow(row, go, owner);
                return;
            }

            ChildRows.Remove(row);
            UpdateTreeLines(item, view, null, null);
            var look = LookFor(go.name);
            var cleanName = look.CleanName;
            var text = look.Text;

            row.AddToClassList(HeaderClass);
            HeaderRows[row] = go;
            WireHover(row);
            PaintRow(row, go);
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
            cleanLabel.style.unityTextAlign = look.Anchor ?? TextAnchor.MiddleLeft;
            cleanLabel.style.unityFontStyleAndWeight = look.Font;
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
            button.tooltip = $"Select the {go.transform.childCount} object(s) in this group";
            button.style.color = text;
            var childrenIcon = UIE.UQueryExtensions.Q<UIE.Image>(button, ChildrenIconName);
            if (childrenIcon != null) childrenIcon.tintColor = text;
            button.style.backgroundColor = new Color(text.r, text.g, text.b, 0.14f);
            button.style.display = go.transform.childCount > 0 ? UIE.DisplayStyle.Flex : UIE.DisplayStyle.None;
        }
#endif

        internal static Color HeaderColor(string cleanName)
        {
            if (HierarchyGroupColors.TryGet(cleanName, out var chosen)) return chosen;

            var key = cleanName.ToUpperInvariant();

            if (HierarchyOrganizerTool.TryGetDefaultColor(key, out var color))
                return color;

            float hue = StableHash(key) % 100 / 100f;
            return Color.HSVToRGB(hue, 0.65f, 0.72f);
        }

        internal static Color TextColorFor(Color background)
        {
            Color.RGBToHSV(background, out float h, out float s, out float v);
            return background.grayscale > 0.7f
                ? Color.HSVToRGB(h, Mathf.Min(1f, s * 1.15f), 0.20f)
                : Color.HSVToRGB(h, s * 0.08f, 1f);
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

    // Gosto de cada um, então fica no EditorPrefs e não vai pro Git
    internal static class HierarchyViewPrefs
    {
        private const string TintKey = "IsaDo3D.Hierarchy.ChildTint";
        private const string TintAlphaKey = "IsaDo3D.Hierarchy.ChildTintAlpha";
        private const string LinesKey = "IsaDo3D.Hierarchy.TreeLines";

        internal const float MinTintAlpha = 0.05f;
        internal const float MaxTintAlpha = 0.6f;

        // Lido uma vez, a Hierarchy pergunta isso em toda linha
        private static bool? _tint;
        private static float? _tintAlpha;
        private static bool? _lines;

        internal static event System.Action Changed;

        // Cor do cabeçalho nas linhas de dentro do grupo
        internal static bool ChildTint
        {
            get => _tint ??= EditorPrefs.GetBool(TintKey, true);
            set
            {
                if (ChildTint == value) return;
                _tint = value;
                EditorPrefs.SetBool(TintKey, value);
                Changed?.Invoke();
            }
        }

        internal static float ChildTintAlpha
        {
            get => _tintAlpha ??= Mathf.Clamp(EditorPrefs.GetFloat(TintAlphaKey, 0.15f), MinTintAlpha, MaxTintAlpha);
            set
            {
                value = Mathf.Clamp(value, MinTintAlpha, MaxTintAlpha);
                if (Mathf.Approximately(ChildTintAlpha, value)) return;
                _tintAlpha = value;
                EditorPrefs.SetFloat(TintAlphaKey, value);
                Changed?.Invoke();
            }
        }

        // Linhas ligando os objetos ao grupo
        internal static bool TreeLines
        {
            get => _lines ??= EditorPrefs.GetBool(LinesKey, true);
            set
            {
                if (TreeLines == value) return;
                _lines = value;
                EditorPrefs.SetBool(LinesKey, value);
                Changed?.Invoke();
            }
        }
    }

    // O que dá pra escolher num cabeçalho. O que fica sem valor usa o padrão
    internal class BannerStyle
    {
        public Color? Background;
        public bool NoBackground;
        public Color? Text;
        public TextAnchor? Align;
        public FontStyle? Font;

        public bool IsDefault => Background == null && !NoBackground && Text == null && Align == null && Font == null;

        public BannerStyle Clone()
        {
            return (BannerStyle)MemberwiseClone();
        }
    }

    // Fica em ProjectSettings, que vai pro Git, então o estilo escolhido é igual pra todo mundo
    internal static class HierarchyGroupColors
    {
        internal const string FilePath = "ProjectSettings/IsaDo3DHierarchyColors.json";

        [System.Serializable]
        private class Entry
        {
            public string name;
            public string color = "";
            public bool noBackground;
            public string textColor = "";
            public string align = "";
            public string font = "";
        }

        [System.Serializable]
        private class Data
        {
            public List<Entry> groups = new List<Entry>();
        }

        private static readonly (string Key, TextAnchor Value)[] AlignNames =
        {
            ("left", TextAnchor.MiddleLeft), ("center", TextAnchor.MiddleCenter), ("right", TextAnchor.MiddleRight),
        };

        private static readonly (string Key, FontStyle Value)[] FontNames =
        {
            ("normal", FontStyle.Normal), ("bold", FontStyle.Bold), ("italic", FontStyle.Italic), ("bolditalic", FontStyle.BoldAndItalic),
        };

        private static Dictionary<string, BannerStyle> _styles;
        private static System.DateTime _loadedStamp;

        internal static event System.Action Changed;

#if UNITY_2022_3_OR_NEWER
        // Pull do Git troca o arquivo com a Unity aberta
        [InitializeOnLoadMethod]
        private static void WatchFile()
        {
            EditorApplication.focusChanged += focused =>
            {
                if (!focused || _styles == null || FileStamp() == _loadedStamp) return;
                _styles = null;
                Changed?.Invoke();
            };
        }
#endif

        private static System.DateTime FileStamp()
        {
            return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
        }

        private static Color? ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color)) return color;
            return null;
        }

        private static string ColorText(Color? color)
        {
            return color.HasValue ? "#" + ColorUtility.ToHtmlStringRGB(color.Value) : "";
        }

        private static Dictionary<string, BannerStyle> Styles
        {
            get
            {
                if (_styles != null) return _styles;

                _styles = new Dictionary<string, BannerStyle>();
                _loadedStamp = FileStamp();
                if (!File.Exists(FilePath)) return _styles;

                try
                {
                    var data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                    if (data?.groups == null) return _styles;

                    foreach (var entry in data.groups)
                    {
                        if (string.IsNullOrEmpty(entry.name)) continue;

                        var style = new BannerStyle
                        {
                            Background = ParseColor(entry.color),
                            NoBackground = entry.noBackground,
                            Text = ParseColor(entry.textColor),
                        };
                        foreach (var (key, value) in AlignNames)
                            if (key == entry.align) style.Align = value;
                        foreach (var (key, value) in FontNames)
                            if (key == entry.font) style.Font = value;

                        if (!style.IsDefault) _styles[entry.name.ToUpperInvariant()] = style;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Hierarchy Organizer] Could not read {FilePath}: {e.Message}");
                }

                return _styles;
            }
        }

        internal static BannerStyle Get(string cleanName)
        {
            return Styles.TryGetValue(HierarchyOrganizerTool.CanonicalGroupKey(cleanName), out var style) ? style : null;
        }

        internal static bool TryGet(string cleanName, out Color color)
        {
            var background = Get(cleanName)?.Background;
            color = background ?? default;
            return background.HasValue;
        }

        internal static void Set(string cleanName, Color color)
        {
            Edit(cleanName, style => style.Background = color);
        }

        internal static void Edit(string cleanName, System.Action<BannerStyle> change)
        {
            var key = HierarchyOrganizerTool.CanonicalGroupKey(cleanName);
            if (!Styles.TryGetValue(key, out var style)) style = new BannerStyle();

            change(style);
            if (style.Background.HasValue) style.Background = Opaque(style.Background.Value);
            if (style.Text.HasValue) style.Text = Opaque(style.Text.Value);

            if (style.IsDefault) Styles.Remove(key);
            else Styles[key] = style;
            Save();
        }

        internal static void Clear(string cleanName)
        {
            if (Styles.Remove(HierarchyOrganizerTool.CanonicalGroupKey(cleanName))) Save();
        }

        // Renomear o grupo leva o estilo junto, se o nome novo ainda não tiver um
        internal static void CopyIfMissing(string fromCleanName, string toCleanName)
        {
            var from = HierarchyOrganizerTool.CanonicalGroupKey(fromCleanName);
            var to = HierarchyOrganizerTool.CanonicalGroupKey(toCleanName);
            if (from == to || Styles.ContainsKey(to) || !Styles.TryGetValue(from, out var style)) return;

            Styles[to] = style.Clone();
            Save();
        }

        internal static Dictionary<string, BannerStyle> Snapshot()
        {
            return Styles.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        }

        internal static void Restore(Dictionary<string, BannerStyle> snapshot)
        {
            _styles = snapshot.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
            Save();
        }

        private static Color Opaque(Color color)
        {
            color.a = 1f;
            return color;
        }

        private static void Save()
        {
            // Sem estilo escolhido o arquivo some, pra não sobrar um JSON vazio no Git
            if (_styles.Count == 0)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            else
            {
                var data = new Data();
                foreach (var pair in _styles.OrderBy(p => p.Key, System.StringComparer.Ordinal))
                {
                    var style = pair.Value;
                    data.groups.Add(new Entry
                    {
                        name = pair.Key,
                        color = ColorText(style.Background),
                        noBackground = style.NoBackground,
                        textColor = ColorText(style.Text),
                        align = style.Align.HasValue ? AlignNames.First(a => a.Value == style.Align.Value).Key : "",
                        font = style.Font.HasValue ? FontNames.First(f => f.Value == style.Font.Value).Key : "",
                    });
                }
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true) + "\n");
            }

            _loadedStamp = FileStamp();
            Changed?.Invoke();
        }
    }
}
