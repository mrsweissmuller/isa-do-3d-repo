using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ArtTools.EnvironmentArt
{
    internal static class ExternalToolLocator
    {
        public static void ConfigurePath(string prefKey, string logPrefix, string toolName, string exeFileName, string defaultRoot)
        {
            var current = EditorPrefs.GetString(prefKey, "");
            var path = EditorUtility.OpenFilePanel($"Select {exeFileName}", string.IsNullOrEmpty(current) ? defaultRoot : Path.GetDirectoryName(current), "exe");
            if (string.IsNullOrEmpty(path)) return;

            EditorPrefs.SetString(prefKey, path);
            Debug.Log($"{logPrefix} {toolName} path saved: {path}");
        }

        public static string GetOrPromptPath(string prefKey, string logPrefix, string toolName, string exeFileName, string defaultRoot, Func<string> autoDetect)
        {
            var saved = EditorPrefs.GetString(prefKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

            var detected = autoDetect?.Invoke();
            if (!string.IsNullOrEmpty(detected))
            {
                EditorPrefs.SetString(prefKey, detected);
                Debug.Log($"{logPrefix} {toolName} detected automatically at: {detected}");
                return detected;
            }

            EditorUtility.DisplayDialog($"Open in {toolName}", $"Could not find {toolName} automatically. Select the executable ({exeFileName}) in the next window.", "OK");
            var path = EditorUtility.OpenFilePanel($"Select {exeFileName}", defaultRoot, "exe");
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

        private static Version ExtractVersion(string folderName)
        {
            var match = Regex.Match(folderName, @"\d+(\.\d+)*");
            if (!match.Success) return new Version(0, 0);

            var value = match.Value.Contains('.') ? match.Value : match.Value + ".0";
            return Version.TryParse(value, out var version) ? version : new Version(0, 0);
        }
    }
}
