using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    // Textura T_, material M_, FBX com mesh SM_, prefab PF_. RenameAsset mantém o GUID, nenhuma referência quebra.
    public static class NamingConventionFixer
    {
        private static readonly string[] KnownPrefixes = { "T_", "M_", "SM_", "SK_", "PF_" };

        // FBX só de animação ou rig tem nome próprio
        private static readonly string[] ModelKeepPrefixes = { "Anim_", "RigBase_" };

        [MenuItem("Isa do 3D/Assets/Corrigir Nomenclatura", true)]
        [MenuItem("Assets/Corrigir Nomenclatura", true)]
        private static bool CanFixSelection() =>
            IsaDo3DToolState.IsOn(ToolIds.NamingFixer) && Selection.assetGUIDs.Length > 0;

        [MenuItem("Assets/Corrigir Nomenclatura", false, 2001)]
        [MenuItem("Isa do 3D/Assets/Corrigir Nomenclatura")]
        private static void FixSelection()
        {
            int renamed = 0;
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                var target = FixedName(path, name);
                if (target == null || target == name) continue;

                var error = AssetDatabase.RenameAsset(path, target);
                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log($"[Isa do 3D] {name} -> {target}");
                    ++renamed;
                }
                else
                {
                    Debug.LogWarning($"[Isa do 3D] {name} não foi renomeado: {error}");
                }
            }
            if (renamed > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[Isa do 3D] {renamed} renomeado(s).");
        }

        // Null quando o tipo não tem convenção. Prefixo de outro tipo sai (material T_Floor vira M_Floor)
        private static string FixedName(string path, string name)
        {
            var prefix = PrefixFor(path, name);
            if (prefix == null) return null;

            var clean = Regex.Replace(name.Trim(), @"\s+", "_");
            var wrong = KnownPrefixes.FirstOrDefault(p => clean.StartsWith(p, System.StringComparison.OrdinalIgnoreCase));
            if (wrong != null) clean = clean.Substring(wrong.Length);
            return prefix + clean;
        }

        private static string PrefixFor(string path, string name)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == typeof(Texture2D)) return "T_";
            if (type == typeof(Material)) return "M_";
            if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return "PF_";
            if (AssetImporter.GetAtPath(path) is ModelImporter)
            {
                if (ModelKeepPrefixes.Any(p => name.StartsWith(p))) return null;
                return AssetDatabase.LoadAllAssetsAtPath(path).Any(a => a is Mesh) ? "SM_" : null;
            }
            return null;
        }
    }
}
