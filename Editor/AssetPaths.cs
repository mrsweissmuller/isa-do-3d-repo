using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class AssetPaths
    {
        // Path.GetFullPath depende do diretório atual, e Packages/ é virtual pra pacote file: ou git
        public static string Absolute(string assetPath)
        {
            var package = assetPath.StartsWith("Packages/") ? PackageInfo.FindForAssetPath(assetPath) : null;
            if (package != null)
            {
                var insidePackage = assetPath.Substring(package.assetPath.Length + 1);
                return Path.GetFullPath(Path.Combine(package.resolvedPath, insidePackage)).Replace('\\', '/');
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace('\\', '/');
        }
    }
}
