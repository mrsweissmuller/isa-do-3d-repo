using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class ExternalToolLocator
    {
        public static void ConfigurePath(string prefKey, string logPrefix, string toolName, string exeFileName, string defaultRoot)
        {
            var current = EditorPrefs.GetString(prefKey, "");
            var path = EditorUtility.OpenFilePanel($"Selecione {exeFileName}", string.IsNullOrEmpty(current) ? defaultRoot : Path.GetDirectoryName(current), "exe");
            if (string.IsNullOrEmpty(path)) return;

            EditorPrefs.SetString(prefKey, path);
            Debug.Log($"{logPrefix} Caminho do {toolName} salvo: {path}");
        }

        public static string GetOrPromptPath(string prefKey, string logPrefix, string toolName, string exeFileName, string defaultRoot, Func<string> autoDetect)
        {
            var saved = EditorPrefs.GetString(prefKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

            var detected = autoDetect?.Invoke();
            if (!string.IsNullOrEmpty(detected))
            {
                EditorPrefs.SetString(prefKey, detected);
                Debug.Log($"{logPrefix} {toolName} detectado automaticamente em: {detected}");
                return detected;
            }

            EditorUtility.DisplayDialog($"Abrir no {toolName}", $"Não encontrei o {toolName} automaticamente. Selecione o executável ({exeFileName}) na próxima janela.", "OK");
            var path = EditorUtility.OpenFilePanel($"Selecione {exeFileName}", defaultRoot, "exe");
            if (string.IsNullOrEmpty(path)) return null;

            EditorPrefs.SetString(prefKey, path);
            return path;
        }

        public static string FindNewestVersionedInstall(string root, string folderPrefix, params string[] exeRelativePath)
        {
            if (!Directory.Exists(root)) return null;

            return Directory.GetDirectories(root)
                .Where(dir => folderPrefix == null || Path.GetFileName(dir).StartsWith(folderPrefix))
                .Select(dir => new
                {
                    Exe = Path.Combine(new[] { dir }.Concat(exeRelativePath).ToArray()),
                    Version = ExtractVersion(Path.GetFileName(dir)),
                })
                .Where(candidate => File.Exists(candidate.Exe))
                .OrderByDescending(candidate => candidate.Version)
                .Select(candidate => candidate.Exe)
                .FirstOrDefault();
        }

        // Comparar como texto põe "4.10" antes de "4.9"
        private static Version ExtractVersion(string folderName)
        {
            var match = Regex.Match(folderName, @"\d+(\.\d+)*");
            if (!match.Success) return new Version(0, 0);

            // Version.TryParse precisa de major.minor
            var value = match.Value.Contains('.') ? match.Value : match.Value + ".0";
            return Version.TryParse(value, out var version) ? version : new Version(0, 0);
        }
    }
}
