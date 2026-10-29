using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IsaDo3D.EnvironmentArt
{
    // X/Textures/T_Nome(_D).png -> X/Materials/M_Nome.mat
    public class TextureMaterialCreator : AssetPostprocessor
    {
        // Mapa que não é cor não ganha material próprio
        private static readonly string[] NonColorSuffixes = { "_N", "_Normal", "_M", "_Mask", "_R", "_AO", "_H", "_E", "_ORM", "_MRA" };
        private static readonly HashSet<string> Pending = new HashSet<string>();

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

        // Só a primeira importação (sem .meta), senão reimportar ou refazer a Library cria material pra textura antiga
        private void OnPreprocessTexture()
        {
            if (!IsaDo3DToolState.IsOn(ToolIds.TextureMaterial)) return;
            if (!assetImporter.importSettingsMissing || !IsColorTexture(assetPath)) return;
            if (File.Exists(MaterialPathFor(assetPath)) || !Pending.Add(assetPath)) return;

            // Dentro do callback de importação não dá pra criar asset
            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            if (Pending.Count == 0) return;
            var paths = new List<string>(Pending);
            Pending.Clear();
            foreach (var path in paths) Create(path);
            AssetDatabase.SaveAssets();
        }

        private static bool IsColorTexture(string path)
        {
            if (!path.StartsWith("Assets/") || !path.Contains("/Textures/")) return false;
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("T_")) return false;
            foreach (var suffix in NonColorSuffixes)
                if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string MaterialPathFor(string texturePath)
        {
            var name = Path.GetFileNameWithoutExtension(texturePath).Substring(2);
            if (name.EndsWith("_D")) name = name.Substring(0, name.Length - 2);
            var dir = Path.GetDirectoryName(texturePath).Replace('\\', '/').Replace("/Textures", "/Materials");
            return $"{dir}/M_{name}.mat";
        }

        private static void Create(string texturePath)
        {
            var materialPath = MaterialPathFor(texturePath);
            if (File.Exists(materialPath)) return;
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

            var dir = Path.GetDirectoryName(materialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
            AssetDatabase.CreateAsset(material, materialPath);
            Debug.Log($"[Isa do 3D] {materialPath} criado a partir de {(template != null ? template.name : shader.name)}.", material);
        }

        private static Shader DefaultShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null && pipeline.defaultMaterial != null ? pipeline.defaultMaterial.shader : Shader.Find("Standard");
        }

        // M_Floor_Wood_01 -> qualquer M_Floor_* da mesma pasta
        private static Material TemplateFor(string materialPath)
        {
            var dir = Path.GetDirectoryName(materialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            var parts = Path.GetFileNameWithoutExtension(materialPath).Split('_');
            if (parts.Length < 2) return null;
            var family = parts[0] + "_" + parts[1] + "_";

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;
                if (Path.GetFileName(path).StartsWith(family)) return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }
    }
}
