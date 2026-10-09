#if ISADO3D_KRITA
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace IsaDo3D.EnvironmentArt
{
    public static class OpenInKrita
    {
        private const string KritaPathPrefKey = "Isa3D.OpenInKrita.KritaPath";
        private const string KritaDefaultRoot = "C:/Program Files";

        // Abre mas não vigia, a Unity não importa .kra
        private const string KritaSourceExtension = ".kra";

        private static readonly string[] OpenableExtensions =
            { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr", ".bmp", ".gif", KritaSourceExtension };

        [MenuItem("Isa do 3D/Krita/Configurar Caminho do Krita...")]
        public static void ConfigureKritaPath()
        {
            ExternalToolLocator.ConfigurePath(KritaPathPrefKey, "[Krita]", "Krita", "krita.exe", KritaDefaultRoot);
        }

        [MenuItem("Assets/Abrir no Krita", true)]
        [MenuItem("GameObject/Abrir no Krita", true)]
        private static bool ValidateOpen()
        {
            return TryResolveTextureAssetPath(Selection.activeObject, out _, out _);
        }

        [MenuItem("Assets/Abrir no Krita")]
        [MenuItem("GameObject/Abrir no Krita", false, 0)]
        [MenuItem("Isa do 3D/Krita/Abrir Selecionado no Krita")]
        public static void OpenSelected()
        {
            if (!TryResolveTextureAssetPath(Selection.activeObject, out var textureAssetPath, out var sourceDescription))
            {
                EditorUtility.DisplayDialog(
                    "Abrir no Krita",
                    "Selecione uma textura, um material ou um objeto que use um material com textura.",
                    "OK");
                return;
            }

            var kritaPath = GetOrPromptKritaPath();
            if (string.IsNullOrEmpty(kritaPath)) return;

            var absoluteTexturePath = Path.GetFullPath(textureAssetPath).Replace('\\', '/');
            if (!File.Exists(absoluteTexturePath))
            {
                Debug.LogError($"[Krita] Arquivo não encontrado no disco: {absoluteTexturePath}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = kritaPath,
                Arguments = $"\"{absoluteTexturePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                Process.Start(psi);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Krita] Falha ao iniciar o Krita em '{kritaPath}': {e.Message}");
                return;
            }

            Debug.Log($"[Krita] Abrindo '{sourceDescription}' ({absoluteTexturePath}) no Krita.");
            StartWatching(textureAssetPath, absoluteTexturePath);
        }

        private static string GetOrPromptKritaPath()
        {
            return ExternalToolLocator.GetOrPromptPath(KritaPathPrefKey, "[Krita]", "Krita", "krita.exe", KritaDefaultRoot, TryAutoDetectKrita);
        }

        private static string TryAutoDetectKrita()
        {
            var candidates = new[]
            {
                "C:/Program Files/Krita (x64)/bin/krita.exe",
                "C:/Program Files/Krita/bin/krita.exe",
                "C:/Program Files (x86)/Krita (x86)/bin/krita.exe",
            };

            return candidates.FirstOrDefault(File.Exists);
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
                Debug.Log($"[Krita] '{material.name}' tem mais textura: {others}. Pra abrir uma dessas, selecione ela no Project.");
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
            TryAdd("textura principal", material.mainTexture);

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
        private const string WatchListSessionKey = "IsaDo3D.OpenInKrita.Watching";

        private static readonly Dictionary<string, WatchedTexture> Watched = new Dictionary<string, WatchedTexture>();
        private static double _nextPoll;

        [MenuItem("Isa do 3D/Krita/Parar de Recarregar ao Salvar", true)]
        private static bool ValidateStopWatching() => Watched.Count > 0;

        [MenuItem("Isa do 3D/Krita/Parar de Recarregar ao Salvar")]
        public static void StopWatching()
        {
            int count = Watched.Count;
            Watched.Clear();
            SaveWatchList();
            EditorApplication.update -= Poll;

            Debug.Log($"[Krita] Parei de vigiar {count} textura(s). Elas continuam abertas no Krita, " +
                      "só não recarregam sozinhas aqui.");
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

            Debug.Log($"[Krita] Vigiando '{Path.GetFileName(assetPath)}'. Salvou no Krita, reimporta aqui.");
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
                    Debug.Log($"[Krita] '{Path.GetFileName(watched.AssetPath)}' sumiu do disco, parei de vigiar.");
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
                Debug.Log($"[Krita] Recarreguei '{Path.GetFileName(watched.AssetPath)}'.");
            }

            if (listChanged) SaveWatchList();
        }

        private static void SaveWatchList()
        {
            // '|' não pode em caminho no Windows
            SessionState.SetString(WatchListSessionKey, string.Join("|", Watched.Keys));
        }

        [InitializeOnLoadMethod]
        private static void RestoreWatchList()
        {
            var saved = SessionState.GetString(WatchListSessionKey, "");
            if (string.IsNullOrEmpty(saved)) return;

            foreach (var assetPath in saved.Split('|'))
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                var absolutePath = Path.GetFullPath(assetPath).Replace('\\', '/');
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
#endif //ISADO3D_KRITA
