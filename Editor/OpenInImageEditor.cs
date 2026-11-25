using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace IsaDo3D.EnvironmentArt
{
    public static class OpenInImageEditor
    {
        // O mesmo que a Unity usa em Preferences > External Tools > Image application
        private const string UnityImageAppPrefKey = "kImagesDefaultApp";

        // De quando a ferramenta só abria o Krita e guardava o caminho dele
        private const string LegacyKritaPathPrefKey = "Isa3D.OpenInKrita.KritaPath";
        private const string KritaSourceExtension = ".kra";

        private static readonly string[] OpenableExtensions =
            { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr", ".bmp", ".gif", KritaSourceExtension };

        // Vazio é sem programa escolhido, aí abre com o que o Windows associa ao arquivo
        internal static string EditorPath
        {
            get
            {
                var chosen = EditorPrefs.GetString(UnityImageAppPrefKey, "");
                if (!string.IsNullOrEmpty(chosen)) return chosen;

                var legacy = EditorPrefs.GetString(LegacyKritaPathPrefKey, "");
                return File.Exists(legacy) ? legacy : "";
            }
        }

        // Grava na preferência da Unity, então o duplo clique numa textura passa a abrir no mesmo programa
        public static void ConfigureEditorPath()
        {
            var current = EditorPath;
            var path = EditorUtility.OpenFilePanel(
                "Select the image editor",
                string.IsNullOrEmpty(current) ? "C:/Program Files" : Path.GetDirectoryName(current),
                "exe");
            if (string.IsNullOrEmpty(path)) return;

            EditorPrefs.SetString(UnityImageAppPrefKey, path);
            Debug.Log($"[Image Editor] Image application set to: {path}");
        }

        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.ImageEditor);

        [MenuItem("Assets/Open in Image Editor", true)]
        [MenuItem("GameObject/Open in Image Editor", true)]
        private static bool ValidateOpen()
        {
            return IsOn() && TryResolveTextureAssetPath(Selection.activeObject, out _, out _);
        }

        [MenuItem("Assets/Open in Image Editor")]
        [MenuItem("GameObject/Open in Image Editor", false, 0)]
        public static void OpenSelected()
        {
            if (!TryResolveTextureAssetPath(Selection.activeObject, out var textureAssetPath, out var sourceDescription))
            {
                EditorUtility.DisplayDialog(
                    "Open in Image Editor",
                    "Select a texture, a material or an object that uses a textured material.",
                    "OK");
                return;
            }

            var absoluteTexturePath = AssetPaths.Absolute(textureAssetPath);
            if (!File.Exists(absoluteTexturePath))
            {
                Debug.LogError($"[Image Editor] File not found on disk: {absoluteTexturePath}");
                return;
            }

            var editorPath = EditorPath;
            bool hasEditor = !string.IsNullOrEmpty(editorPath);
            if (hasEditor && !File.Exists(editorPath))
            {
                Debug.LogError($"[Image Editor] Image application not found: {editorPath}. Set it again in Art Tools/Settings...");
                return;
            }

            // O fonte .kra só o Krita abre. Sem programa escolhido o Windows manda ele pra quem estiver associado.
            // Com fonte, a textura continua vigiada pra recarregar quando for exportada
            var kritaSourcePath = !hasEditor || IsKrita(editorPath) ? FindKritaSource(absoluteTexturePath) : null;
            var fileToOpen = kritaSourcePath ?? absoluteTexturePath;

            if (!Launch(editorPath, fileToOpen)) return;

            Debug.Log(kritaSourcePath != null
                ? $"[Image Editor] Opening the source of '{sourceDescription}' ({kritaSourcePath})."
                : $"[Image Editor] Opening '{sourceDescription}' ({absoluteTexturePath}).");
            StartWatching(textureAssetPath, absoluteTexturePath);
        }

        private static bool IsKrita(string editorPath)
        {
            return Path.GetFileNameWithoutExtension(editorPath).ToLowerInvariant().Contains("krita");
        }

        private static bool Launch(string editorPath, string fileToOpen)
        {
            if (string.IsNullOrEmpty(editorPath))
            {
                EditorUtility.OpenWithDefaultApp(fileToOpen);
                return true;
            }

            var psi = new ProcessStartInfo
            {
                FileName = editorPath,
                Arguments = $"\"{fileToOpen}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                Process.Start(psi);
                return true;
            }
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException)
            {
                Debug.LogError($"[Image Editor] Failed to start '{editorPath}': {e.Message}");
                return false;
            }
        }

        // Escrito pelo plugin "Exportar para Unity" do Krita: { "map": { "<fonte .kra>": "<textura exportada>" } }
        private const string KritaExportMapPath = "krita/pykrita/unity_export/unity_export.json";

        private static readonly Regex JsonStringPair =
            new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");

        private static string FindKritaSource(string absoluteTexturePath)
        {
            if (Path.GetExtension(absoluteTexturePath).ToLowerInvariant() == KritaSourceExtension) return null;

            var mapPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), KritaExportMapPath);
            if (!File.Exists(mapPath)) return null;

            try
            {
                var wanted = ComparablePath(absoluteTexturePath);

                foreach (Match pair in JsonStringPair.Matches(File.ReadAllText(mapPath)))
                {
                    var source = Regex.Unescape(pair.Groups[1].Value);
                    if (Path.GetExtension(source).ToLowerInvariant() != KritaSourceExtension) continue;
                    if (ComparablePath(Regex.Unescape(pair.Groups[2].Value)) != wanted) continue;
                    if (!File.Exists(source)) continue;

                    // O plugin guarda o caminho do fonte em minúsculas
                    var onDisk = Directory.GetFiles(Path.GetDirectoryName(source), Path.GetFileName(source));
                    return onDisk.Length > 0 ? onDisk[0] : source;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Image Editor] Could not read the source map at '{mapPath}': {e.Message}");
            }

            return null;
        }

        private static string ComparablePath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
        }

        private static bool TryResolveTextureAssetPath(Object selected, out string assetPath, out string sourceDescription)
        {
            assetPath = null;
            sourceDescription = null;
            if (selected == null) return false;

            var directPath = AssetDatabase.GetAssetPath(selected);
            if (IsOpenableFile(directPath))
            {
                assetPath = directPath;
                sourceDescription = Path.GetFileName(directPath);
                return true;
            }

            if (selected is Material material)
                return TryResolveFromMaterial(material, material.name, out assetPath, out sourceDescription);

            var go = selected as GameObject;
            if (go == null) return false;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var rendererMaterial in renderer.sharedMaterials)
                {
                    if (rendererMaterial == null) continue;

                    var origin = renderer.gameObject == go
                        ? $"{go.name} / {rendererMaterial.name}"
                        : $"{go.name} > {renderer.gameObject.name} / {rendererMaterial.name}";

                    if (TryResolveFromMaterial(rendererMaterial, origin, out assetPath, out sourceDescription))
                        return true;
                }
            }

            return false;
        }

        private static bool TryResolveFromMaterial(Material material, string origin, out string assetPath, out string sourceDescription)
        {
            assetPath = null;
            sourceDescription = null;

            var textures = CollectTextures(material);
            if (textures.Count == 0) return false;

            assetPath = textures[0].Path;
            sourceDescription = $"{origin} > {textures[0].Property}";

            if (textures.Count > 1)
            {
                var others = string.Join(", ", textures.Skip(1).Select(t => $"{t.Property} ({Path.GetFileName(t.Path)})"));
                Debug.Log($"[Image Editor] '{material.name}' has more textures: {others}. To open one of them, select it in the Project.");
            }

            return true;
        }

        private struct MaterialTexture
        {
            public string Property;
            public string Path;
        }

        private static List<MaterialTexture> CollectTextures(Material material)
        {
            var found = new List<MaterialTexture>();
            var seenPaths = new HashSet<string>();

            void TryAdd(string property, Texture texture)
            {
                if (texture == null) return;

                var path = AssetDatabase.GetAssetPath(texture);
                if (!IsOpenableFile(path)) return;
                if (!seenPaths.Add(path)) return;

                found.Add(new MaterialTexture { Property = property, Path = path });
            }

            // mainTexture cobre o _MainTex e o _BaseMap
            TryAdd("main texture", material.mainTexture);

            foreach (var property in material.GetTexturePropertyNames())
                TryAdd(property, material.GetTexture(property));

            return found;
        }

        private static bool IsOpenableFile(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return OpenableExtensions.Contains(ext);
        }

        private class WatchedTexture
        {
            public string AssetPath;
            public string AbsolutePath;
            public DateTime ImportedWrite;
            public DateTime PendingWrite;
        }

        private const double PollIntervalSeconds = 1.0;

        // SessionState sobrevive à recompilação
        private const string WatchListSessionKey = "Isa3D.OpenInKrita.Watching";

        private static readonly Dictionary<string, WatchedTexture> Watched = new Dictionary<string, WatchedTexture>();
        private static double _nextPoll;

        // Pra janela de Settings mostrar quantas estão vigiadas e oferecer o Stop
        internal static int WatchedCount => Watched.Count;
        internal static event Action WatchListChanged;

        public static void StopWatching()
        {
            int count = Watched.Count;
            Watched.Clear();
            SaveWatchList();
            EditorApplication.update -= Poll;

            Debug.Log($"[Image Editor] Stopped watching {count} texture(s). They stay open in the editor, " +
                      "they just no longer reload here on their own.");
        }

        private static void StartWatching(string assetPath, string absolutePath)
        {
            if (Path.GetExtension(assetPath).ToLowerInvariant() == KritaSourceExtension) return;

            if (!assetPath.StartsWith("Assets/")) return;

            if (Watched.ContainsKey(assetPath)) return;

            Watched[assetPath] = new WatchedTexture
            {
                AssetPath = assetPath,
                AbsolutePath = absolutePath,
                ImportedWrite = File.GetLastWriteTimeUtc(absolutePath),
            };

            SaveWatchList();
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;

            Debug.Log($"[Image Editor] Watching '{Path.GetFileName(assetPath)}'. Save it in the editor and it reimports here.");
        }

        private static void Poll()
        {
            if (Watched.Count == 0)
            {
                EditorApplication.update -= Poll;
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollIntervalSeconds;

            bool listChanged = false;

            foreach (var watched in Watched.Values.ToList())
            {
                if (!File.Exists(watched.AbsolutePath))
                {
                    Watched.Remove(watched.AssetPath);
                    listChanged = true;
                    Debug.Log($"[Image Editor] '{Path.GetFileName(watched.AssetPath)}' is gone from disk, stopped watching.");
                    continue;
                }

                var write = File.GetLastWriteTimeUtc(watched.AbsolutePath);
                if (write == watched.ImportedWrite)
                {
                    watched.PendingWrite = default;
                    continue;
                }

                // O Krita muda a data quando começa a salvar, só reimporta quando ela parar de mudar
                if (write != watched.PendingWrite)
                {
                    watched.PendingWrite = write;
                    continue;
                }

                watched.ImportedWrite = write;
                watched.PendingWrite = default;

                AssetDatabase.ImportAsset(watched.AssetPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[Image Editor] Reloaded '{Path.GetFileName(watched.AssetPath)}'.");
            }

            if (listChanged) SaveWatchList();
        }

        private static void SaveWatchList()
        {
            // '|' não pode em caminho no Windows
            SessionState.SetString(WatchListSessionKey, string.Join("|", Watched.Keys));
            WatchListChanged?.Invoke();
        }

        [InitializeOnLoadMethod]
        private static void RestoreWatchList()
        {
            var saved = SessionState.GetString(WatchListSessionKey, "");
            if (string.IsNullOrEmpty(saved)) return;

            foreach (var assetPath in saved.Split('|'))
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                var absolutePath = AssetPaths.Absolute(assetPath);
                if (!File.Exists(absolutePath)) continue;

                Watched[assetPath] = new WatchedTexture
                {
                    AssetPath = assetPath,
                    AbsolutePath = absolutePath,

                    // O que foi salvo durante a recompilação a Unity já importou
                    ImportedWrite = File.GetLastWriteTimeUtc(absolutePath),
                };
            }

            SaveWatchList();

            if (Watched.Count == 0) return;

            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }
    }
}
