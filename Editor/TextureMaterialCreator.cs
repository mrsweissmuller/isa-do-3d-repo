using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IsaDo3D.EnvironmentArt
{
    // X/Textures/TEX_Nome(_D).png -> X/Materials/MAT_Nome.mat, só pelo clique direito. Os prefixos vêm do
    // AssetNaming; textura com o prefixo antigo (T_) ou sem prefixo também vale. Tudo que a ferramenta decide
    // (recusou, já existia, de onde copiou, o que não conseguiu ligar) sai no Console.
    public static class TextureMaterialCreator
    {
        // Mapa que não é cor não ganha material próprio. Comparado com maiúscula e minúscula como está
        private static readonly string[] NonColorSuffixes = { "_N", "_Normal", "_M", "_Mask", "_R", "_AO", "_H", "_E", "_ORM", "_MRA" };
        private static readonly string[] ColorSuffixes = { "_D", "_Albedo", "_BaseColor" };
        private static readonly string[] NormalSuffixes = { "_N", "_Normal" };

        // Onde o shader guarda a cor, do mais comum pro menos
        private static readonly string[] MainTextureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" };
        private static readonly string[] NormalProperties = { "_BumpMap", "_NormalMap" };

        // Mapa que é de uma textura só: herdado do material molde estaria errado, então sai. O resto do molde
        // (ruído, máscara compartilhada de shader próprio) fica e é listado no Console
        private static readonly Dictionary<string, string> PerTextureProperties = new Dictionary<string, string>
        {
            { "_BumpMap", "_NORMALMAP" }, { "_NormalMap", "_NORMALMAP" }, { "_MetallicGlossMap", "_METALLICSPECGLOSSMAP" },
            { "_SpecGlossMap", "_METALLICSPECGLOSSMAP" }, { "_OcclusionMap", "_OCCLUSIONMAP" }, { "_ParallaxMap", "_PARALLAXMAP" },
            { "_EmissionMap", null }, { "_DetailAlbedoMap", null }, { "_DetailNormalMap", null }, { "_DetailMask", null },
        };

        [MenuItem("Isa do 3D/Assets/Criar Material da Textura", true)]
        [MenuItem("Assets/Criar Material da Textura", true)]
        private static bool CanCreateForSelection() =>
            IsaDo3DToolState.IsOn(ToolIds.TextureMaterial) && Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

        [MenuItem("Assets/Criar Material da Textura", false, 2000)]
        [MenuItem("Isa do 3D/Assets/Criar Material da Textura")]
        private static void CreateForSelection()
        {
            int created = 0, existing = 0, refused = 0;
            foreach (var texture in Selection.GetFiltered<Texture2D>(SelectionMode.Assets))
            {
                var path = AssetDatabase.GetAssetPath(texture);
                var refusal = RefusalFor(path);
                if (refusal != null)
                {
                    Debug.LogWarning($"[Isa do 3D] {path}: sem material, {refusal}.", texture);
                    ++refused;
                    continue;
                }
                if (Create(path)) ++created;
                else ++existing;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Isa do 3D] Criar Material da Textura: {created} criado(s), {existing} que já existiam, {refused} recusado(s).");
        }

        // Null quando a textura pode ganhar material; senão, o motivo
        private static string RefusalFor(string path)
        {
            if (!ProjectAssets.IsOurs(path)) return "é asset de terceiros ou de pacote";
            if (TexturesFolderIndex(path.Split('/')) < 0) return "não está dentro de uma pasta chamada Textures";
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var suffix in NonColorSuffixes)
                if (name.EndsWith(suffix, System.StringComparison.Ordinal)) return $"o sufixo {suffix} é de mapa que não é cor";
            return null;
        }

        // Índice da última pasta chamada exatamente Textures (Textures_Shared e afins não contam)
        private static int TexturesFolderIndex(string[] parts)
        {
            for (int i = parts.Length - 2; i >= 0; --i)
                if (parts[i] == "Textures") return i;
            return -1;
        }

        // TEX_Nome_D -> Nome (sem o prefixo de tipo e sem o sufixo de cor)
        private static string BaseName(string texturePath)
        {
            var name = AssetNaming.StripTypePrefix(Path.GetFileNameWithoutExtension(texturePath));
            foreach (var suffix in ColorSuffixes)
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, System.StringComparison.Ordinal))
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        // X/Textures/Sub/TEX_Nome_D.png -> X/Materials/Sub/MAT_Nome.mat
        private static string MaterialPathFor(string texturePath)
        {
            var parts = texturePath.Split('/');
            parts[TexturesFolderIndex(parts)] = "Materials";
            parts[parts.Length - 1] = $"{AssetNaming.Material}{BaseName(texturePath)}.mat";
            return string.Join("/", parts);
        }

        // True quando criou; false quando o material já existia
        private static bool Create(string texturePath)
        {
            var materialPath = MaterialPathFor(texturePath);
            var already = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (already != null)
            {
                Debug.Log($"[Isa do 3D] {materialPath} já existe, não mexi nele.", already);
                return false;
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            // Cópia de um irmão da mesma família já vem com o shader e os ajustes certos do projeto
            var template = TemplateFor(materialPath);
            var shader = template != null ? template.shader : DefaultShader();
            if (shader == null)
            {
                Debug.LogWarning($"[Isa do 3D] Sem shader padrão, {texturePath} ficou sem material.", texture);
                return false;
            }
            var material = template != null ? new Material(template) : new Material(shader);
            var notes = new List<string>();

            var mainProperties = SetMainTexture(material, texture);
            if (mainProperties.Count == 0)
                notes.Add($"o shader {shader.name} não tem textura principal que eu conheça: liga a textura à mão");

            // Normal de mesmo nome ao lado da textura
            var normal = SiblingMap(texturePath, NormalSuffixes);
            var normalSet = false;
            if (normal != null)
            {
                foreach (var property in NormalProperties)
                {
                    if (!material.HasProperty(property)) continue;
                    material.SetTexture(property, normal);
                    material.EnableKeyword("_NORMALMAP");
                    normalSet = true;
                }
                notes.Add(normalSet ? $"normal ligado: {normal.name}" : $"achei o normal {normal.name}, mas o shader não tem onde ligar");
            }

            // O que veio do molde: mapa que é de uma textura só sai, o resto fica e é avisado
            if (template != null)
            {
                var removed = new List<string>();
                var kept = new List<string>();
                foreach (var property in material.GetTexturePropertyNames())
                {
                    if (mainProperties.Contains(property)) continue;
                    if (normalSet && System.Array.IndexOf(NormalProperties, property) >= 0) continue;
                    var inherited = material.GetTexture(property);
                    if (inherited == null) continue;

                    if (PerTextureProperties.TryGetValue(property, out var keyword))
                    {
                        material.SetTexture(property, null);
                        if (keyword != null) material.DisableKeyword(keyword);
                        removed.Add($"{property} ({inherited.name})");
                    }
                    else kept.Add($"{property} ({inherited.name})");
                }
                if (removed.Count > 0) notes.Add($"tirei do molde: {string.Join(", ", removed)}");
                if (kept.Count > 0) notes.Add($"continuam as do molde: {string.Join(", ", kept)}");
            }

            // Outros mapas de mesmo nome que eu não sei em qual propriedade do shader entram
            foreach (var other in OtherSiblingMaps(texturePath))
                notes.Add($"achei {other}, não liguei: confere se entra neste material");

            ProjectAssets.EnsureFolder(Path.GetDirectoryName(materialPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(material, materialPath);

            var source = template != null ? $"cópia de {template.name} (shader, cor e tiling vêm dele)" : $"shader {shader.name}";
            var message = $"[Isa do 3D] {materialPath} criado, {source}.";
            if (notes.Count > 0) message += "\n  - " + string.Join("\n  - ", notes);
            if (mainProperties.Count == 0) Debug.LogWarning(message, material);
            else Debug.Log(message, material);
            return true;
        }

        // Liga a textura em toda propriedade de cor que o shader tem. Devolve as que ligou; vazio quando nenhuma
        private static List<string> SetMainTexture(Material material, Texture2D texture)
        {
            var set = new List<string>();
            foreach (var property in MainTextureProperties)
            {
                if (!material.HasProperty(property)) continue;
                material.SetTexture(property, texture);
                set.Add(property);
            }
            if (set.Count > 0) return set;

            // Shader próprio: a propriedade marcada como [MainTexture]
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) == 0) continue;
                var property = shader.GetPropertyName(i);
                material.SetTexture(property, texture);
                set.Add(property);
            }
            return set;
        }

        // TEX_Nome_D.png -> a textura TEX_Nome<sufixo> da mesma pasta, com qualquer extensão; null se não tem
        private static Texture2D SiblingMap(string texturePath, string[] suffixes)
        {
            var dir = Path.GetDirectoryName(texturePath).Replace('\\', '/');
            var baseName = BaseName(texturePath);
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;
                var name = AssetNaming.StripTypePrefix(Path.GetFileNameWithoutExtension(path));
                foreach (var suffix in suffixes)
                    if (name == baseName + suffix) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        // Nome dos outros mapas de mesmo nome na pasta (máscara, AO, emissivo...), fora cor e normal
        private static List<string> OtherSiblingMaps(string texturePath)
        {
            var result = new List<string>();
            var dir = Path.GetDirectoryName(texturePath).Replace('\\', '/');
            var baseName = BaseName(texturePath);
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == texturePath || Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;
                var name = AssetNaming.StripTypePrefix(Path.GetFileNameWithoutExtension(path));
                foreach (var suffix in NonColorSuffixes)
                {
                    if (name != baseName + suffix || System.Array.IndexOf(NormalSuffixes, suffix) >= 0) continue;
                    result.Add(Path.GetFileName(path));
                }
            }
            return result;
        }

        private static Shader DefaultShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null && pipeline.defaultMaterial != null ? pipeline.defaultMaterial.shader : Shader.Find("Standard");
        }

        // MAT_Floor_Wood_01 -> o MAT_Floor_* da mesma pasta com mais partes do nome em comum (MAT_Floor_Wood_Plank
        // antes de MAT_Floor_Tile), empate por ordem de nome, pra sair sempre o mesmo
        private static Material TemplateFor(string materialPath)
        {
            var dir = Path.GetDirectoryName(materialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            var parts = Path.GetFileNameWithoutExtension(materialPath).Split('_');
            if (parts.Length < 2) return null;

            string best = null; int bestScore = 1;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;
                var other = Path.GetFileNameWithoutExtension(path).Split('_');
                int score = 0;
                while (score < parts.Length && score < other.Length && parts[score] == other[score]) ++score;
                if (score > bestScore || (score == bestScore && best != null && string.CompareOrdinal(path, best) < 0))
                {
                    best = path; bestScore = score;
                }
            }
            return best == null ? null : AssetDatabase.LoadAssetAtPath<Material>(best);
        }
    }
}
