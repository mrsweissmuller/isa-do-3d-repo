using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ArtTools.EnvironmentArt
{
    internal static class AssetPaths
    {
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
