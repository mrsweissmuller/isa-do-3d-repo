using System;
using System.IO;

namespace IsaDo3D.EnvironmentArt
{
    // Nome único: com nome fixo a segunda mesh sobrescrevia o script da primeira antes do DCC ler
    internal static class TempScriptFile
    {
        private const string Prefix = "isado3d_";

        // Arquivo recente pode ainda estar sendo aberto pelo DCC
        private static readonly TimeSpan MinAgeToDelete = TimeSpan.FromHours(1);

        public static string Write(string nameHint, string extension, string contents)
        {
            DeleteOldFiles();

            var fileName = $"{Prefix}{nameHint}_{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(Path.GetTempPath(), fileName).Replace('\\', '/');

            File.WriteAllText(path, contents);
            return path;
        }

        public static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static void DeleteOldFiles()
        {
            var cutoff = DateTime.Now - MinAgeToDelete;
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Prefix + "*"))
                if (File.GetLastWriteTime(file) < cutoff)
                    TryDelete(file);
        }
    }
}
