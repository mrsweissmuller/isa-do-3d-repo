using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    // Textura TEX_, material MAT_, FBX com mesh SM_ (skinned SK_), prefab PF_ (AssetNaming). Vale pro que está
    // selecionado no Project, pasta inclusive (tudo que tem dentro). Antes de renomear mostra a lista "de -> para"
    // e pede confirmação: renomear asset não tem desfazer.
    // RenameAsset mantém o GUID, nenhuma referência por GUID quebra; o nome do objeto raiz (prefab, modelo) muda
    // junto, então o que procura pelo nome para de achar (par SM_/PF_, mapa do Krita, arquivo de origem).
    public static class NamingConventionFixer
    {
        private const int PreviewLines = 15;

        private struct Rename
        {
            public string Path;
            public string From;
            public string To;
        }

        [MenuItem("Isa do 3D/Assets/Corrigir Nomenclatura", true)]
        [MenuItem("Assets/Corrigir Nomenclatura", true)]
        private static bool CanFixSelection() =>
            IsaDo3DToolState.IsOn(ToolIds.NamingFixer) && Selection.assetGUIDs.Length > 0;

        [MenuItem("Assets/Corrigir Nomenclatura", false, 2001)]
        [MenuItem("Isa do 3D/Assets/Corrigir Nomenclatura")]
        private static void FixSelection()
        {
            var paths = SelectedAssetPaths();
            var plan = new List<Rename>();
            var conflicts = new List<string>();
            int thirdParty = 0, noConvention = 0, alreadyRight = 0;

            // Destino já tomado: por outro asset do disco ou por outro item desta mesma leva
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (!ProjectAssets.IsOurs(path)) { ++thirdParty; continue; }

                var name = Path.GetFileNameWithoutExtension(path);
                var target = FixedName(path, name);
                if (target == null) { ++noConvention; continue; }
                if (target == name) { ++alreadyRight; continue; }

                var targetPath = Path.GetDirectoryName(path).Replace('\\', '/') + "/" + target + Path.GetExtension(path);
                var sameFileOtherCase = string.Equals(targetPath, path, System.StringComparison.OrdinalIgnoreCase);
                if (!taken.Add(targetPath) || (!sameFileOtherCase && File.Exists(targetPath)))
                {
                    conflicts.Add($"{name} -> {target} (já existe {targetPath})");
                    continue;
                }
                plan.Add(new Rename { Path = path, From = name, To = target });
            }

            var skipped = new StringBuilder();
            if (alreadyRight > 0) skipped.Append($"{alreadyRight} já no padrão. ");
            if (noConvention > 0) skipped.Append($"{noConvention} de tipo sem convenção. ");
            if (thirdParty > 0) skipped.Append($"{thirdParty} de terceiros, não mexo. ");
            foreach (var conflict in conflicts) Debug.LogWarning($"[Isa do 3D] Não dá pra renomear: {conflict}");

            if (plan.Count == 0)
            {
                Debug.Log($"[Isa do 3D] Nada pra renomear em {paths.Count} asset(s). {skipped}" +
                          (conflicts.Count > 0 ? $"{conflicts.Count} com nome de destino já usado (ver avisos)." : ""));
                return;
            }

            // A lista inteira vai pro Console; o diálogo mostra só o começo
            var full = new StringBuilder();
            foreach (var item in plan) full.AppendLine($"  {item.From}  ->  {item.To}    ({item.Path})");
            Debug.Log($"[Isa do 3D] Corrigir Nomenclatura, {plan.Count} pra renomear:\n{full}");

            var preview = new StringBuilder();
            foreach (var item in plan.Take(PreviewLines)) preview.AppendLine($"{item.From}  ->  {item.To}");
            if (plan.Count > PreviewLines) preview.AppendLine($"... e mais {plan.Count - PreviewLines} (lista inteira no Console)");
            preview.AppendLine();
            if (skipped.Length > 0) preview.AppendLine(skipped.ToString());
            if (conflicts.Count > 0) preview.AppendLine($"{conflicts.Count} ficam de fora: nome de destino já usado.");
            preview.AppendLine("Renomear não tem desfazer. As referências continuam valendo (o GUID não muda), mas o que " +
                               "procura pelo nome para de achar: o par SM_/PF_ dos builders, o mapa do Krita, o arquivo de origem.");

            if (!EditorUtility.DisplayDialog("Corrigir Nomenclatura", preview.ToString(), $"Renomear {plan.Count}", "Cancelar"))
            {
                Debug.Log("[Isa do 3D] Corrigir Nomenclatura cancelado, nada foi renomeado.");
                return;
            }

            int renamed = 0;
            foreach (var item in plan)
            {
                var error = AssetDatabase.RenameAsset(item.Path, item.To);
                if (string.IsNullOrEmpty(error)) ++renamed;
                else Debug.LogWarning($"[Isa do 3D] {item.From} não foi renomeado: {error}");
            }
            if (renamed > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[Isa do 3D] {renamed} de {plan.Count} renomeado(s).");
        }

        // Os assets selecionados e tudo que tem dentro das pastas selecionadas, sem repetir e sem as pastas
        private static List<string> SelectedAssetPaths()
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            void Add(string path)
            {
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
                if (seen.Add(path)) result.Add(path);
            }

            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var inside in AssetDatabase.FindAssets("", new[] { path }))
                        Add(AssetDatabase.GUIDToAssetPath(inside));
                }
                else Add(path);
            }
            result.Sort(string.CompareOrdinal);
            return result;
        }

        // Null quando o tipo não tem convenção. Prefixo de outro tipo ou da convenção antiga sai
        // (material T_Floor vira MAT_Floor, textura T_Floor vira TEX_Floor)
        private static string FixedName(string path, string name)
        {
            var prefix = PrefixFor(path, name);
            if (prefix == null) return null;

            var clean = Regex.Replace(name.Trim(), @"\s+", "_");
            return prefix + AssetNaming.StripTypePrefix(clean);
        }

        private static string PrefixFor(string path, string name)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == typeof(Texture2D)) return AssetNaming.Texture;
            if (type == typeof(Material)) return AssetNaming.Material;
            if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return AssetNaming.Prefab;
            if (AssetImporter.GetAtPath(path) is ModelImporter)
            {
                if (AssetNaming.ModelKeepPrefixes.Any(p => name.StartsWith(p))) return null;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) return null;
                if (model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return AssetNaming.SkinnedMesh;
                return model.GetComponentInChildren<MeshFilter>(true) != null ? AssetNaming.StaticMesh : null;
            }
            return null;
        }
    }
}
