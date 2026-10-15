using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class MeshAssetResolver
    {
        public static bool TryResolveMeshAssetPath(Object selected, string[] modelExtensions, out string assetPath, out string sourceDescription)
        {
            assetPath = null;
            sourceDescription = null;
            if (selected == null) return false;

            var directPath = AssetDatabase.GetAssetPath(selected);
            if (IsModelFile(directPath, modelExtensions))
            {
                assetPath = directPath;
                sourceDescription = Path.GetFileName(directPath);
                return true;
            }

            var go = selected as GameObject;
            if (go == null) return false;

            Mesh foundMesh = null;
            GameObject foundOn = null;
            FindFirstMesh(go.transform, ref foundMesh, ref foundOn);
            if (foundMesh == null) return false;

            var meshPath = AssetDatabase.GetAssetPath(foundMesh);
            if (!IsModelFile(meshPath, modelExtensions)) return false;

            assetPath = meshPath;
            sourceDescription = foundOn == go ? go.name : $"{go.name} > {foundOn.name}";
            return true;
        }

        private static void FindFirstMesh(Transform node, ref Mesh foundMesh, ref GameObject foundOn)
        {
            if (foundMesh != null) return;

            if (node.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
            {
                foundMesh = mf.sharedMesh;
                foundOn = node.gameObject;
                return;
            }
            if (node.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh != null)
            {
                foundMesh = smr.sharedMesh;
                foundOn = node.gameObject;
                return;
            }

            for (int i = 0; i < node.childCount; ++i)
            {
                FindFirstMesh(node.GetChild(i), ref foundMesh, ref foundOn);
                if (foundMesh != null) return;
            }
        }

        private static bool IsModelFile(string assetPath, string[] modelExtensions)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return modelExtensions.Contains(ext);
        }
    }
}
