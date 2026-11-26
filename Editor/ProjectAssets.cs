using UnityEditor;

namespace ArtTools.EnvironmentArt
{
    internal static class ProjectAssets
    {
        private static readonly string[] ThirdPartyRoots =
        {
            "Assets/ThirdParty/", "Assets/Plugins/", "Assets/Standard Assets/", "Assets/StreamingAssets/",
        };

        public static bool IsOurs(string assetPath)
        {
            if (!assetPath.StartsWith("Assets/")) return false;
            foreach (var root in ThirdPartyRoots)
                if (assetPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

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

namespace ArtTools.EnvironmentArt
{
    internal static class AssetNaming
    {
        public const string Texture = "TEX_";
        public const string Material = "MAT_";

        private static readonly string[] TypePrefixes = { "TEX_", "MAT_", "SM_", "SK_", "PF_", "T_", "M_" };

        public static string StripTypePrefix(string name)
        {
            for (var guard = 0; guard < 4; ++guard)
            {
                string found = null;
                foreach (var prefix in TypePrefixes)
                {
                    if (name.Length > prefix.Length && name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = prefix;
                        break;
                    }
                }
                if (found == null) break;
                name = name.Substring(found.Length);
            }
            return name;
        }
    }
}
