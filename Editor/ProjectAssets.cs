using UnityEditor;

namespace IsaDo3D.EnvironmentArt
{
    internal static class ProjectAssets
    {
        // Pasta de terceiros carrega coisa por caminho e nome; renomear ou criar asset lá quebra o plugin
        private static readonly string[] ThirdPartyRoots =
        {
            "Assets/ThirdParty/", "Assets/Plugins/", "Assets/Standard Assets/", "Assets/StreamingAssets/",
        };

        // Asset do projeto que a gente pode mexer: dentro de Assets/ e fora das pastas de terceiros
        public static bool IsOurs(string assetPath)
        {
            if (!assetPath.StartsWith("Assets/")) return false;
            foreach (var root in ThirdPartyRoots)
                if (assetPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // CreateFolder só faz um nível
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var slash = folder.LastIndexOf('/');
            var parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
