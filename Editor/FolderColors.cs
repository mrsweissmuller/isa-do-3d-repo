using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    // Pinta o ícone da pasta na janela Project e põe um selo no canto, pela janelinha do clique direito. A cor vale pras
    // pastas de dentro até uma delas ter a própria; o selo é só da pasta. Não usa API interna da Unity: o ícone
    // pintado é o que a própria Unity entrega pra pasta, clareado uma vez
    [InitializeOnLoad]
    internal static class FolderColors
    {
        private const string Menu = "Assets/Folder Color...";
        private const string ToolsMenu = "Art Tools/Folder Color...";

        // As cores que já vêm na janela; as que a pessoa guarda ficam no FolderColorState
        internal static readonly (string Name, string Hex)[] Presets =
        {
            ("Red", "#FF7B7B"), ("Orange", "#FF9B7B"), ("Amber", "#FFD07B"), ("Yellow", "#F5F07B"),
            ("Green", "#9DF0A0"), ("Teal", "#7BD4B4"), ("Blue", "#7BB8F0"), ("Indigo", "#7B8FF0"),
            ("Purple", "#B07BF0"), ("Pink", "#F07BB8"), ("Gray", "#A0A0A0"),
        };

        // A chave é o que fica salvo
        internal static readonly (string Key, string Name)[] Badges =
        {
            ("mesh", "Mesh"), ("material", "Material"), ("texture", "Texture"), ("prefab", "Prefab"),
            ("scene", "Scene"), ("script", "Script"), ("animation", "Animation"), ("audio", "Audio"),
            ("shader", "Shader"), ("star", "Star"),
        };

        // Menu de contexto não diz onde o mouse estava, então fica anotado o último clique direito num item
        private const double ClickMemorySeconds = 120;
        private static Vector2 _lastRightClick;
        private static double _lastRightClickTime = double.MinValue;

        private struct Look
        {
            public bool HasColor;
            public Color Color;
            public Texture2D Icon;
            public Texture Badge;
        }

        // Do que já foi desenhado, arquivo incluso, pra só resolver herança uma vez por item
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>();

        // Ícone da Unity -> cópia em tons de branco, que aceita qualquer cor por cima
        private static readonly Dictionary<Texture2D, Texture2D> Tintable = new Dictionary<Texture2D, Texture2D>();
        private static readonly HashSet<Texture2D> Pending = new HashSet<Texture2D>();
        private static readonly Dictionary<string, Texture> BadgeIcons = new Dictionary<string, Texture>();

        private static bool _buildScheduled;
        private static bool _errorReported;

        static FolderColors()
        {
            EditorApplication.projectWindowItemOnGUI += OnItemGUI;
            EditorApplication.projectChanged += Refresh;
            FolderColorState.Changed += Refresh;
            IsaDo3DToolState.Changed += Refresh;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyTextures;
        }

        private static void Refresh()
        {
            Looks.Clear();
            EditorApplication.RepaintProjectWindow();
        }

        private static void OnItemGUI(string guid, Rect rect)
        {
            // Erro que sai daqui deixa a janela Project inteira em branco, então nenhum escapa
            try
            {
                var current = Event.current;
                if (current.rawType == EventType.MouseDown && current.button == 1)
                {
                    _lastRightClick = GUIUtility.GUIToScreenPoint(current.mousePosition);
                    _lastRightClickTime = EditorApplication.timeSinceStartup;
                }
                if (current.type == EventType.Repaint) Draw(guid, rect);
            }
            catch (Exception e)
            {
                if (_errorReported) return;
                _errorReported = true;
                Debug.LogException(e);
            }
        }

        private static void Draw(string guid, Rect rect)
        {
            if (!IsaDo3DToolState.IsOn(ToolIds.FolderColors)) return;

            if (!Looks.TryGetValue(guid, out var look))
            {
                look = Resolve(guid);
                Looks[guid] = look;
            }
            if (!look.HasColor && look.Badge == null) return;

            // Lista: ícone do tamanho da linha. Na lista da coluna da direita ele fica 3 px pra dentro
            bool list = rect.height <= 20f;
            var icon = list
                ? new Rect(rect.x + (Mathf.Approximately(rect.x, 14f) ? 3f : 0f), rect.y, rect.height, rect.height)
                : new Rect(rect.x, rect.y, rect.width, rect.width);

            var before = GUI.color;
            if (look.HasColor && look.Icon != null)
            {
                if (Tintable.TryGetValue(look.Icon, out var white))
                {
                    if (white != null)
                    {
                        GUI.color = look.Color;
                        GUI.DrawTexture(icon, white, ScaleMode.ScaleToFit);
                    }
                }
                else
                {
                    Schedule(look.Icon);
                }
            }

            if (look.Badge != null)
            {
                float size = list ? 10f : Mathf.Round(icon.width * 0.42f);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(icon.xMax - size + 1f, icon.yMax - size + 1f, size, size), look.Badge, ScaleMode.ScaleToFit);
            }
            GUI.color = before;
        }

        private static Look Resolve(string guid)
        {
            var look = new Look();
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(path)) return look;

            var own = FolderColorState.Find(guid);
            if (own != null && own.badge != "") look.Badge = BadgeIcon(own.badge);

            // Sobe até a primeira pasta que decide a cor
            var folder = path;
            var entry = own;
            while (true)
            {
                if (entry != null && entry.color != FolderColorState.Inherit)
                {
                    look.HasColor = entry.color != FolderColorState.NoColor
                                    && ColorUtility.TryParseHtmlString(entry.color, out look.Color);
                    break;
                }

                int slash = folder.LastIndexOf('/');
                if (slash < 0) break;
                folder = folder.Substring(0, slash);
                entry = FolderColorState.Find(AssetDatabase.AssetPathToGUID(folder));
            }

            // Vem o ícone de pasta vazia quando ela está vazia
            if (look.HasColor) look.Icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            return look;
        }

        // Feito fora do OnGUI, porque trocar o alvo de desenho no meio do repaint bagunça a janela
        private static void Schedule(Texture2D icon)
        {
            Pending.Add(icon);
            if (_buildScheduled) return;
            _buildScheduled = true;
            EditorApplication.delayCall += BuildPending;
        }

        private static void BuildPending()
        {
            _buildScheduled = false;
            foreach (var icon in Pending)
            {
                if (icon == null) continue;
                try
                {
                    Tintable[icon] = MakeTintable(icon);
                }
                catch (Exception e)
                {
                    // Fica null pra não tentar de novo a cada repaint
                    Tintable[icon] = null;
                    Debug.LogException(e);
                }
            }
            Pending.Clear();
            EditorApplication.RepaintProjectWindow();
        }

        // O ícone da Unity não deixa ler os pixels, então passa por um RenderTexture. Fica só o sombreado,
        // puxado pro branco, com a transparência original
        private static Texture2D MakeTintable(Texture2D source)
        {
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
            }

            var pixels = copy.GetPixels32();
            int brightest = 1;
            foreach (var p in pixels)
                if (p.a > 0) brightest = Mathf.Max(brightest, Mathf.Max(p.r, Mathf.Max(p.g, p.b)));

            for (int i = 0; i < pixels.Length; ++i)
            {
                var p = pixels[i];
                byte value = (byte)(Mathf.Max(p.r, Mathf.Max(p.g, p.b)) * 255 / brightest);
                pixels[i] = new Color32(value, value, value, p.a);
            }
            copy.SetPixels32(pixels);
            copy.Apply();
            return copy;
        }

        private static void DestroyTextures()
        {
            foreach (var texture in Tintable.Values)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            Tintable.Clear();
        }

        internal static Texture BadgeIcon(string badge)
        {
            if (BadgeIcons.TryGetValue(badge, out var icon)) return icon;

            switch (badge)
            {
                case "mesh": icon = AssetPreview.GetMiniTypeThumbnail(typeof(Mesh)); break;
                case "material": icon = AssetPreview.GetMiniTypeThumbnail(typeof(Material)); break;
                case "texture": icon = AssetPreview.GetMiniTypeThumbnail(typeof(Texture2D)); break;
                case "prefab": icon = EditorGUIUtility.IconContent("Prefab Icon").image; break;
                case "scene": icon = AssetPreview.GetMiniTypeThumbnail(typeof(SceneAsset)); break;
                case "script": icon = AssetPreview.GetMiniTypeThumbnail(typeof(MonoScript)); break;
                case "animation": icon = AssetPreview.GetMiniTypeThumbnail(typeof(AnimationClip)); break;
                case "audio": icon = AssetPreview.GetMiniTypeThumbnail(typeof(AudioClip)); break;
                case "shader": icon = AssetPreview.GetMiniTypeThumbnail(typeof(Shader)); break;
                case "star": icon = EditorGUIUtility.IconContent("Favorite Icon").image; break;
            }
            BadgeIcons[badge] = icon;
            return icon;
        }

        private static string[] SelectedFolders() =>
            Selection.assetGUIDs.Where(guid => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid))).ToArray();

        [MenuItem(Menu, true)]
        private static bool CanOpen() => IsaDo3DToolState.IsOn(ToolIds.FolderColors) && SelectedFolders().Length > 0;

        [MenuItem(Menu, false, 2100)]
        private static void OpenAtMouse()
        {
            // Aberto pelo menu Assets de cima não tem clique direito recente
            bool recent = EditorApplication.timeSinceStartup - _lastRightClickTime <= ClickMemorySeconds;
            FolderColorPopup.Open(SelectedFolders(), recent ? _lastRightClick : FocusedCenter());
        }

        [MenuItem(ToolsMenu, true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.FolderColors);

        // Pelo menu Art Tools e pelo botão Open da janela de Settings, onde não tem mouse em cima de pasta
        [MenuItem(ToolsMenu, false, 3)]
        public static void Open()
        {
            var folders = SelectedFolders();
            if (folders.Length == 0)
            {
                EditorUtility.DisplayDialog("Folder Color", "Select one or more folders in the Project window first.", "OK");
                return;
            }
            FolderColorPopup.Open(folders, FocusedCenter());
        }

        private static Vector2 FocusedCenter()
        {
            var host = EditorWindow.focusedWindow != null
                ? EditorWindow.focusedWindow.position
                : EditorGUIUtility.GetMainWindowPosition();
            return host.center - new Vector2(124f, 110f);
        }
    }
}
