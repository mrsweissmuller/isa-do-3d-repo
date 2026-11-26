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

namespace ArtTools.EnvironmentArt
{
    public partial class HierarchyOrganizerTool : EditorWindow
    {
        internal const int AccentHex = 0xB85B88;

        public const string HeaderPrefix = "---";

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
            public string[] Aliases = System.Array.Empty<string>();
        }

        private static readonly DefaultGroup[] DefaultGroups =
        {
            new DefaultGroup
            {
                Name = "SETUP", Color = FromHex(0x3d64c7),
                Tooltip = "Cameras, managers and core scene systems.",
                Aliases = new[] { "CORE" },
            },
            new DefaultGroup
            {
                Name = "LIGHTING", Color = FromHex(0x4b8f27),
                Tooltip = "Lights, reflection probes, light probes and lighting volumes.",
            },
            new DefaultGroup
            {
                Name = "VFX", Color = FromHex(0xbb7723),
                Tooltip = "Particle systems and visual effects.",
            },
            new DefaultGroup
            {
                Name = "AUDIO", Color = FromHex(0x7137a5),
                Tooltip = "Audio sources, ambiences and music.",
            },
            new DefaultGroup
            {
                Name = "UI", Color = FromHex(0xb85b88),
                Tooltip = "Canvases and interface elements.",
            },
            new DefaultGroup
            {
                Name = "GAMEPLAY", Color = FromHex(0x539ba5),
                Tooltip = "Level design objects: spawns, interactables and triggers.",
            },
            new DefaultGroup
            {
                Name = "ENVIRONMENT", Color = FromHex(0xb74843),
                Tooltip = "Static scenery: terrain, architecture and static meshes.",
            },
            new DefaultGroup
            {
                Name = "PROPS", Color = FromHex(0xb1992f),
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
        private static bool IsOn() => ArtToolsState.IsOn(ToolIds.HierarchyOrganizer);

        [MenuItem("Art Tools/Hierarchy Organizer", false, 2)]
        public static void Open()
        {
            GetWindow<HierarchyOrganizerTool>("Hierarchy Organizer");
        }

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

            bool keepSlots = SceneManager.GetActiveScene().GetRootGameObjects().Any(go => IsTitle(go.name));
            var slots = keepSlots
                ? ordered.Select(go => go.transform.GetSiblingIndex()).OrderBy(i => i).ToList()
                : Enumerable.Range(0, ordered.Count).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
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
            Undo.SetSiblingIndex(transform, index, "Reorder group");
#else
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

        private static string[] NameTokens(string name)
        {
            var spaced = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant();
            return Regex.Split(spaced, "[^a-z0-9]+");
        }

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

        private static List<(GameObject Object, string Destination)> PlanAutoOrganize()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(go => !go.name.StartsWith(HeaderPrefix) && !IsTitle(go.name))
                .Select(go => (go, ClassifyObject(go)))
                .ToList();
        }

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

        private static string BaseName(string name)
        {
            var match = Regex.Match(name.Trim(), @"^(.*?)(?:\s*\(\d+\))*$");
            var baseName = match.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(baseName) ? name.Trim() : baseName;
        }

        private static string Stem(string name)
        {
            var stem = Regex.Replace(name.Trim(), @"(?:\s*\(\d+\)|[._][0-9]+)+$", "");
            return string.IsNullOrEmpty(stem) ? name.Trim() : stem;
        }

        private static string BaseNameOf(GameObject go)
        {
            var fallback = BaseName(go.name);
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return fallback;

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(assetPath)) return fallback;

            var prefabName = Path.GetFileNameWithoutExtension(assetPath);
            return Stem(go.name) == Stem(prefabName) ? prefabName : fallback;
        }

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

            foreach (var sameBase in groups.OrderBy(g => g.Count()))
            {
                var objects = sameBase.ToList();
                string format = objects.Count >= 100 ? "000" : "00";
                int number = 0;

                foreach (var go in objects)
                {
                    string newName = sameBase.Key;

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
        internal static Color HierarchyHeaderColor(string cleanName)
        {
            return HierarchyViewPrefs.Adjust(HeaderColor(cleanName));
        }

        private static Color PanelColor =>
            EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);

        private static Color OverPanel(Color background)
        {
            var mixed = Color.Lerp(PanelColor, background, background.a);
            mixed.a = 1f;
            return mixed;
        }

        private static GUIStyle _headerStyle;

        internal class HeaderLook
        {
            public string CleanName;
            public Color Background;
            public bool HasBackground;
            public Color Text;
            public TextAnchor? Anchor;
            public FontStyle Font;
            public string Tooltip;
        }

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
                Text = style?.Text ?? (hasBackground ? TextColorFor(OverPanel(background)) : TitleColor),
                Anchor = style?.Align,
                Font = style?.Font ?? FontStyle.Bold,
                Tooltip = HierarchyOrganizerTool.TooltipFor(cleanName),
            };
            Looks[rawName] = look;
            return look;
        }

        private static readonly GUIContent HeaderLabel = new GUIContent();
        private static readonly GUIContent GroupTip = new GUIContent(" ");
        private static readonly GUIContent ChildrenButtonTip = new GUIContent(" ");
        private static Texture _childrenIcon;
        private static Texture ChildrenIcon =>
            _childrenIcon ??= EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image;

        private static bool HeadersOn =>
            ArtToolsState.IsOn(ToolIds.HierarchyOrganizer) && ArtToolsState.IsOn(ToolIds.HierarchyHeaders);

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
                var owner = FindHeaderAncestor(go);
                if (owner != null && !Selection.Contains(go)) EditorGUI.DrawRect(rect, ChildTint(owner));
                return;
            }

            var look = LookFor(go.name);
            if (!look.HasBackground || look.Background.a < 1f) EditorGUI.DrawRect(rect, RowCoverColor(go));
            if (look.HasBackground) EditorGUI.DrawRect(rect, look.Background);

            _headerStyle ??= new GUIStyle(EditorStyles.label);
            _headerStyle.alignment = look.Anchor ?? TextAnchor.MiddleCenter;
            _headerStyle.fontStyle = look.Font;
            _headerStyle.normal.textColor = look.Text;

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
            return PanelColor;
        }

        private static Color SeparatorColor =>
            EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.32f, 0.32f, 0.32f);

        private static GUIStyle _titleStyle;
        private static readonly GUIContent TitleLabel = new GUIContent();

        private static void DrawTitle(GameObject go, Rect rect)
        {
            rect.xMin = 32f;

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
        private const string HeaderClass = "arttools-header";
        private const string ChildrenButtonName = "arttools-select-children";
        private const string ChildrenIconName = "arttools-select-children-icon";
        private const string CleanNameLabelName = "arttools-clean-name";

        private const string FrozenCellClass = "unity-multi-column-view__cell--frozen";

        private static readonly Dictionary<UIE.VisualElement, (GameObject Group, UIE.Label Name, UIE.Button Button)> HeaderRows =
            new Dictionary<UIE.VisualElement, (GameObject Group, UIE.Label Name, UIE.Button Button)>();
        private static readonly List<UIE.VisualElement> DeadRows = new List<UIE.VisualElement>();
        private static UIE.VisualElement _hoveredRow;

        private const string HoverWiredClass = "arttools-hover-wired";

        private static readonly Dictionary<UIE.VisualElement, (GameObject Object, GameObject Group)> ChildRows =
            new Dictionary<UIE.VisualElement, (GameObject Object, GameObject Group)>();

        private const string TreeLinesName = "arttools-tree-lines";
        private const string TitleBoxName = "arttools-title";
        private const string TitleLabelName = "arttools-title-label";

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

                item.RowContainer.Add(box);

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

        private const float FallbackIndent = 14f;
        private const float ArrowGap = 6f;

        private class TreeLineState
        {
            public GameObject Object;
            public GameObject Group;
            public UIE.VisualElement Toggle;
            public UIE.VisualElement Icon;
        }

        private static readonly Dictionary<int, float> LineXByDepth = new Dictionary<int, float>();

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

        private static void PaintText(UIE.Label name, UIE.Button button, HeaderLook look)
        {
            name.style.color = look.Text;
            name.style.unityTextAlign = look.Anchor ?? TextAnchor.MiddleLeft;
            name.style.unityFontStyleAndWeight = look.Font;

            button.style.color = look.Text;
            button.style.backgroundColor = new Color(look.Text.r, look.Text.g, look.Text.b, 0.14f);
            var childrenIcon = UIE.UQueryExtensions.Q<UIE.Image>(button, ChildrenIconName);
            if (childrenIcon != null) childrenIcon.tintColor = look.Text;
        }

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

                var header = pair.Value;
                if (header.Group == null) continue;

                PaintRow(pair.Key, header.Group);
                PaintText(header.Name, header.Button, LookFor(header.Group.name));
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
            if (HeaderRows.TryGetValue(row, out var header))
            {
                if (header.Group != null) PaintRow(row, header.Group);
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

        private static void OnBindViewItem(UHE.HierarchyWindow window, UH.HierarchyView view, UH.HierarchyViewItem item)
        {
            GameObject go = null;
            var node = item.Node;
            if (node != UH.HierarchyNode.Null && item.Handler is UHE.HierarchyGameObjectHandler handler)
                go = handler.GetGameObject(node);

            var row = item.RowContainer;
            var button = UIE.UQueryExtensions.Q<UIE.Button>(item.RightCustomContainer, ChildrenButtonName);
            var nameHost = item.Name.parent;
            if (nameHost == null) return;

            var cleanLabel = UIE.UQueryExtensions.Q<UIE.Label>(nameHost, CleanNameLabelName);

            var titleBox = UIE.UQueryExtensions.Q<UIE.VisualElement>(row, TitleBoxName);
            if (titleBox != null) titleBox.style.display = UIE.DisplayStyle.None;

            if (go == null || !HeadersOn || !go.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix))
            {
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

            row.AddToClassList(HeaderClass);
            WireHover(row);
            PaintRow(row, go);
            item.Icon.style.display = UIE.DisplayStyle.None;
            item.OverlayIcon.style.display = UIE.DisplayStyle.None;

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
            PaintText(cleanLabel, button, look);
            HeaderRows[row] = (go, cleanLabel, button);
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

        private static uint StableHash(string s)
        {
            uint hash = 2166136261;
            foreach (char c in s)
                hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    internal static class HierarchyViewPrefs
    {
        internal const string FilePath = "UserSettings/ArtToolsHierarchyView.json";

        internal const string TintKey = "ArtTools.Hierarchy.ChildTint";
        internal const string TintAlphaKey = "ArtTools.Hierarchy.ChildTintAlpha";
        internal const string LinesKey = "ArtTools.Hierarchy.TreeLines";
        private const string OpacityKey = "ArtTools.Hierarchy.BannerOpacity";
        private const string SaturationKey = "ArtTools.Hierarchy.BannerSaturation";
        private const string LightnessKey = "ArtTools.Hierarchy.BannerLightness";

        internal const float MinTintAlpha = 0.05f;
        internal const float MaxTintAlpha = 0.6f;

        internal const float MinOpacity = 0.15f;
        internal const float MaxSaturation = 2f;
        internal const float MaxLightness = 0.5f;

        [System.Serializable]
        private class Saved
        {
            public bool childTint = true;
            public float childTintAlpha = 0.15f;
            public bool treeLines = true;
            public float opacity = 1f;
            public float saturation = 1f;
            public float lightness;
        }

        private static Saved _saved;

        internal static event System.Action Changed;

        private static Saved Current => _saved ??= Load();

        private static Saved Load()
        {
            Saved loaded = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    loaded = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath));
                }
                catch (System.Exception e) when (e is IOException || e is System.ArgumentException)
                {
                    Debug.LogWarning($"[Hierarchy Organizer] Could not read {FilePath}: {e.Message}");
                }
            }

            loaded ??= FromMachinePrefs();
            loaded.childTintAlpha = Mathf.Clamp(loaded.childTintAlpha, MinTintAlpha, MaxTintAlpha);
            loaded.opacity = Mathf.Clamp(loaded.opacity, MinOpacity, 1f);
            loaded.saturation = Mathf.Clamp(loaded.saturation, 0f, MaxSaturation);
            loaded.lightness = Mathf.Clamp(loaded.lightness, -MaxLightness, MaxLightness);
            return loaded;
        }

        private static Saved FromMachinePrefs()
        {
            var defaults = new Saved();
            return new Saved
            {
                childTint = EditorPrefs.GetBool(TintKey, defaults.childTint),
                childTintAlpha = EditorPrefs.GetFloat(TintAlphaKey, defaults.childTintAlpha),
                treeLines = EditorPrefs.GetBool(LinesKey, defaults.treeLines),
                opacity = EditorPrefs.GetFloat(OpacityKey, defaults.opacity),
                saturation = EditorPrefs.GetFloat(SaturationKey, defaults.saturation),
                lightness = EditorPrefs.GetFloat(LightnessKey, defaults.lightness),
            };
        }

        private static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true) + "\n");
            Changed?.Invoke();
        }

        private static void Store(ref bool saved, bool value)
        {
            if (saved == value) return;
            saved = value;
            Save();
        }

        private static void Store(ref float saved, float value)
        {
            if (Mathf.Approximately(saved, value)) return;
            saved = value;
            Save();
        }

        internal static bool ChildTint
        {
            get => Current.childTint;
            set => Store(ref Current.childTint, value);
        }

        internal static float ChildTintAlpha
        {
            get => Current.childTintAlpha;
            set => Store(ref Current.childTintAlpha, Mathf.Clamp(value, MinTintAlpha, MaxTintAlpha));
        }

        internal static bool TreeLines
        {
            get => Current.treeLines;
            set => Store(ref Current.treeLines, value);
        }

        internal static float BannerOpacity
        {
            get => Current.opacity;
            set => Store(ref Current.opacity, Mathf.Clamp(value, MinOpacity, 1f));
        }

        internal static float BannerSaturation
        {
            get => Current.saturation;
            set => Store(ref Current.saturation, Mathf.Clamp(value, 0f, MaxSaturation));
        }

        internal static float BannerLightness
        {
            get => Current.lightness;
            set => Store(ref Current.lightness, Mathf.Clamp(value, -MaxLightness, MaxLightness));
        }

        internal static bool BannerAdjusted =>
            BannerOpacity < 1f || !Mathf.Approximately(BannerSaturation, 1f) || !Mathf.Approximately(BannerLightness, 0f);

        internal static void ResetBannerAdjust()
        {
            if (!BannerAdjusted) return;

            Current.opacity = 1f;
            Current.saturation = 1f;
            Current.lightness = 0f;
            Save();
        }

        internal static Color Adjust(Color color)
        {
            float gray = color.grayscale;
            float saturation = BannerSaturation;
            var adjusted = new Color(
                Mathf.Clamp01(gray + (color.r - gray) * saturation),
                Mathf.Clamp01(gray + (color.g - gray) * saturation),
                Mathf.Clamp01(gray + (color.b - gray) * saturation));

            float lightness = BannerLightness;
            adjusted = Color.Lerp(adjusted, lightness < 0f ? Color.black : Color.white, Mathf.Abs(lightness));
            adjusted.a = BannerOpacity;
            return adjusted;
        }
    }

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

    internal static class HierarchyGroupColors
    {
        internal const string FilePath = "UserSettings/ArtToolsHierarchyColors.json";

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

        internal static event System.Action Changed;

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

        internal static void SetBackgrounds(List<string> cleanNames, System.Func<int, Color> backgroundAt)
        {
            for (int i = 0; i < cleanNames.Count; i++)
            {
                var key = HierarchyOrganizerTool.CanonicalGroupKey(cleanNames[i]);
                if (!Styles.TryGetValue(key, out var style)) Styles[key] = style = new BannerStyle();

                style.Background = Opaque(backgroundAt(i));
                style.Text = null;
            }

            Save();
        }

        internal static void Clear(string cleanName)
        {
            if (Styles.Remove(HierarchyOrganizerTool.CanonicalGroupKey(cleanName))) Save();
        }

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

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true) + "\n");
            Changed?.Invoke();
        }
    }
}
