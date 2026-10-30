using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IsaDo3D.EnvironmentArt
{
    // X/Textures/T_Nome(_D).png -> X/Materials/M_Nome.mat, só pelo clique direito
    public static class TextureMaterialCreator
    {
        // Mapa que não é cor não ganha material próprio
        private static readonly string[] NonColorSuffixes = { "_N", "_Normal", "_M", "_Mask", "_R", "_AO", "_H", "_E", "_ORM", "_MRA" };

        [MenuItem("Isa do 3D/Assets/Criar Material da Textura", true)]
        [MenuItem("Assets/Criar Material da Textura", true)]
        private static bool CanCreateForSelection() =>
            IsaDo3DToolState.IsOn(ToolIds.TextureMaterial) && Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

        [MenuItem("Assets/Criar Material da Textura", false, 2000)]
        [MenuItem("Isa do 3D/Assets/Criar Material da Textura")]
        private static void CreateForSelection()
        {
            foreach (var texture in Selection.GetFiltered<Texture2D>(SelectionMode.Assets))
            {
                var path = AssetDatabase.GetAssetPath(texture);
                if (IsColorTexture(path)) Create(path);
                else Debug.LogWarning($"[Isa do 3D] {path} não é textura de cor T_ dentro de uma pasta Textures.", texture);
            }
            AssetDatabase.SaveAssets();
        }

        private static bool IsColorTexture(string path)
        {
            if (!ProjectAssets.IsOurs(path) || TexturesFolderIndex(path.Split('/')) < 0) return false;
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("T_")) return false;
            foreach (var suffix in NonColorSuffixes)
                if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // Índice da última pasta chamada exatamente Textures (Textures_Shared e afins não contam)
        private static int TexturesFolderIndex(string[] parts)
        {
            for (int i = parts.Length - 2; i >= 0; --i)
                if (parts[i] == "Textures") return i;
            return -1;
        }

        // X/Textures/Sub/T_Nome_D.png -> X/Materials/Sub/M_Nome.mat
        private static string MaterialPathFor(string texturePath)
        {
            var name = Path.GetFileNameWithoutExtension(texturePath).Substring(2);
            if (name.EndsWith("_D")) name = name.Substring(0, name.Length - 2);
            var parts = texturePath.Split('/');
            parts[TexturesFolderIndex(parts)] = "Materials";
            parts[parts.Length - 1] = $"M_{name}.mat";
            return string.Join("/", parts);
        }

        private static void Create(string texturePath)
        {
            var materialPath = MaterialPathFor(texturePath);
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) return;

            // Cópia de um irmão da mesma família já vem com o shader e os ajustes certos do projeto
            var template = TemplateFor(materialPath);
            var shader = template != null ? template.shader : DefaultShader();
            if (shader == null)
            {
                Debug.LogWarning($"[Isa do 3D] Sem shader padrão, {texturePath} ficou sem material.");
                return;
            }
            var material = template != null ? new Material(template) : new Material(shader);

            // Propriedade escondida tipo _MainTex também guarda a textura do molde
            var old = template != null ? template.mainTexture : null;
            foreach (var property in material.GetTexturePropertyNames())
                if (old != null && material.GetTexture(property) == old) material.SetTexture(property, texture);
            material.mainTexture = texture;

            ProjectAssets.EnsureFolder(Path.GetDirectoryName(materialPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(material, materialPath);
            Debug.Log($"[Isa do 3D] {materialPath} criado a partir de {(template != null ? template.name : shader.name)}.", material);
        }

        private static Shader DefaultShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null && pipeline.defaultMaterial != null ? pipeline.defaultMaterial.shader : Shader.Find("Standard");
        }

        // M_Floor_Wood_01 -> o M_Floor_* da mesma pasta com mais partes do nome em comum (M_Floor_Wood_Plank antes
        // de M_Floor_Tile), empate por ordem de nome, pra sair sempre o mesmo
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
