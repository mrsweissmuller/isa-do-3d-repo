using System;
using System.Collections.Generic;
using System.IO;

namespace IsaDo3D.EnvironmentArt
{
    // Nome único: com nome fixo a segunda mesh sobrescrevia o script da primeira antes do DCC ler
    internal static class TempScriptFile
    {
        private const string Prefix = "isado3d_";

        // Arquivo recente pode ainda estar sendo aberto pelo DCC
        private static readonly TimeSpan MinAgeToDelete = TimeSpan.FromHours(1);

        // Avisa uma vez por arquivo, senão repete a cada mesh aberta
        private static readonly HashSet<string> WarnedStuck = new HashSet<string>();

        public static string Write(string nameHint, string extension, string contents)
        {
            DeleteOldFiles();

            var fileName = $"{Prefix}{nameHint}_{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(Path.GetTempPath(), fileName).Replace('\\', '/');

            File.WriteAllText(path, contents);
            return path;
        }

        // Falso quando o DCC ainda segura o arquivo
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
                UnityEngine.Debug.LogWarning($"[Isa do 3D] {stuck} script(s) temporário(s) em uso há mais de 1h em {Path.GetTempPath()}, ficam pra próxima.");
        }
    }
}
