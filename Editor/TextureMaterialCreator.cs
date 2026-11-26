using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArtTools.EnvironmentArt
{
    public static class TextureMaterialCreator
    {
        private static readonly string[] NonColorSuffixes = { "_N", "_Normal", "_M", "_Mask", "_R", "_AO", "_H", "_E", "_ORM", "_MRA" };
        private static readonly string[] ColorSuffixes = { "_D", "_Albedo", "_BaseColor" };
        private static readonly string[] NormalSuffixes = { "_N", "_Normal" };

        private static readonly string[] MainTextureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" };
        private static readonly string[] NormalProperties = { "_BumpMap", "_NormalMap" };

        private static readonly Dictionary<string, string> PerTextureProperties = new Dictionary<string, string>
        {
            { "_BumpMap", "_NORMALMAP" }, { "_NormalMap", "_NORMALMAP" }, { "_MetallicGlossMap", "_METALLICSPECGLOSSMAP" },
            { "_SpecGlossMap", "_METALLICSPECGLOSSMAP" }, { "_OcclusionMap", "_OCCLUSIONMAP" }, { "_ParallaxMap", "_PARALLAXMAP" },
            { "_EmissionMap", null }, { "_DetailAlbedoMap", null }, { "_DetailNormalMap", null }, { "_DetailMask", null },
        };

        [MenuItem("Assets/Create Material from Texture", true)]
        private static bool CanCreateForSelection() =>
            ArtToolsState.IsOn(ToolIds.TextureMaterial) && Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

        [MenuItem("Assets/Create Material from Texture", false, 2000)]
        private static void CreateForSelection()
        {
            int created = 0, existing = 0, refused = 0;
            foreach (var texture in Selection.GetFiltered<Texture2D>(SelectionMode.Assets))
            {
                var path = AssetDatabase.GetAssetPath(texture);
                var refusal = RefusalFor(path);
                if (refusal != null)
                {
                    Debug.LogWarning($"[Art Tools] {path}: no material, {refusal}.", texture);
                    ++refused;
                    continue;
                }
                if (Create(path)) ++created;
                else ++existing;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art Tools] Create Material from Texture: {created} created, {existing} already existed, {refused} refused.");
        }

        private static string RefusalFor(string path)
        {
            if (!ProjectAssets.IsOurs(path)) return "it is a third-party or package asset";
            if (TexturesFolderIndex(path.Split('/')) < 0) return "it is not inside a folder named Textures";
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var suffix in NonColorSuffixes)
                if (name.EndsWith(suffix, System.StringComparison.Ordinal)) return $"the {suffix} suffix is for a non-color map";
            return null;
        }

        private static int TexturesFolderIndex(string[] parts)
        {
            for (int i = parts.Length - 2; i >= 0; --i)
                if (parts[i] == "Textures") return i;
            return -1;
        }

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

        private static string MaterialPathFor(string texturePath)
        {
            var parts = texturePath.Split('/');
            parts[TexturesFolderIndex(parts)] = "Materials";
            parts[parts.Length - 1] = $"{AssetNaming.Material}{BaseName(texturePath)}.mat";
            return string.Join("/", parts);
        }

        private static bool Create(string texturePath)
        {
            var materialPath = MaterialPathFor(texturePath);
            var already = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (already != null)
            {
                Debug.Log($"[Art Tools] {materialPath} already exists, left untouched.", already);
                return false;
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var template = TemplateFor(materialPath);
            var shader = template != null ? template.shader : DefaultShader();
            if (shader == null)
            {
                Debug.LogWarning($"[Art Tools] No default shader, {texturePath} got no material.", texture);
                return false;
            }
            var material = template != null ? new Material(template) : new Material(shader);
            var notes = new List<string>();

            var mainProperties = SetMainTexture(material, texture);
            if (mainProperties.Count == 0)
                notes.Add($"shader {shader.name} has no main texture property this tool knows: assign the texture by hand");

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
                notes.Add(normalSet ? $"normal assigned: {normal.name}" : $"found the normal {normal.name}, but the shader has no slot for it");
            }

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
                if (removed.Count > 0) notes.Add($"removed from the template: {string.Join(", ", removed)}");
                if (kept.Count > 0) notes.Add($"kept from the template: {string.Join(", ", kept)}");
            }

            foreach (var other in OtherSiblingMaps(texturePath))
                notes.Add($"found {other}, not assigned: check whether it belongs in this material");

            ProjectAssets.EnsureFolder(Path.GetDirectoryName(materialPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(material, materialPath);

            var source = template != null ? $"copy of {template.name} (shader, color and tiling come from it)" : $"shader {shader.name}";
            var message = $"[Art Tools] Created {materialPath}, {source}.";
            if (notes.Count > 0) message += "\n  - " + string.Join("\n  - ", notes);
            if (mainProperties.Count == 0) Debug.LogWarning(message, material);
            else Debug.Log(message, material);
            return true;
        }

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

        private static Material TemplateFor(string materialPath)
        {
            var dir = Path.GetDirectoryName(materialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            var parts = AssetNaming.StripTypePrefix(Path.GetFileNameWithoutExtension(materialPath)).Split('_');
            if (parts.Length < 2) return null;

            string best = null; int bestScore = 1;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;
                var other = AssetNaming.StripTypePrefix(Path.GetFileNameWithoutExtension(path)).Split('_');
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
