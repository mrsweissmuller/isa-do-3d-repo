using System;
using System.Collections.Generic;
using System.IO;

namespace ArtTools.EnvironmentArt
{
    internal static class TempScriptFile
    {
        private const string Prefix = "arttools_";

        private static readonly TimeSpan MinAgeToDelete = TimeSpan.FromHours(1);

        private static readonly HashSet<string> WarnedStuck = new HashSet<string>();

        public static string Write(string nameHint, string extension, string contents)
        {
            DeleteOldFiles();

            var fileName = $"{Prefix}{nameHint}_{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(Path.GetTempPath(), fileName).Replace('\\', '/');

            File.WriteAllText(path, contents);
            return path;
        }

        public static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void DeleteOldFiles()
        {
            var cutoff = DateTime.Now - MinAgeToDelete;
            int stuck = 0;
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Prefix + "*"))
            {
                if (File.GetLastWriteTime(file) >= cutoff) continue;
                if (TryDelete(file) || !WarnedStuck.Add(file)) continue;
                stuck++;
            }

            if (stuck > 0)
                UnityEngine.Debug.LogWarning($"[Art Tools] {stuck} temporary script(s) in {Path.GetTempPath()} have been in use for over 1h, left for next time.");
        }
    }
}
