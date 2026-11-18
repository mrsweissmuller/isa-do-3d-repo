using UnityEditor;

namespace IsaDo3D.EnvironmentArt
{
    internal static class ProjectAssets
    {
        // Pasta de terceiros carrega coisa por caminho e nome; renomear ou criar asset lá quebra o plugin
        private static readonly string[] ThirdPartyRoots =
        {
            "Assets/ThirdParty/", "Assets/Plugins/", "Assets/Standard Assets/", "Assets/StreamingAssets/",
        };

        // Asset do projeto que a gente pode mexer: dentro de Assets/ e fora das pastas de terceiros
        public static bool IsOurs(string assetPath)
        {
            if (!assetPath.StartsWith("Assets/")) return false;
            foreach (var root in ThirdPartyRoots)
                if (assetPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // CreateFolder só faz um nível
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

namespace IsaDo3D.EnvironmentArt
{
    // Convenção de nomes do projeto, num lugar só: Criar Material da Textura e Corrigir Nomenclatura leem daqui.
    // Mudou a convenção, muda aqui.
    internal static class AssetNaming
    {
        public const string Texture = "TEX_";
        public const string Material = "MAT_";
        public const string StaticMesh = "SM_";
        public const string SkinnedMesh = "SK_";
        public const string Prefab = "PF_";

        // Prefixo de tipo, atual ou antigo (T_, M_). Os mais longos primeiro, pra MAT_ não ser lido como M_
        private static readonly string[] TypePrefixes = { "TEX_", "MAT_", "SM_", "SK_", "PF_", "T_", "M_" };

        // FBX que tem nome próprio e não vira SM_/SK_: parede destrutível, animação, rig
        public static readonly string[] ModelKeepPrefixes = { "DM_", "Anim_", "RigBase_" };

        // TEX_Floor -> Floor. Tira também prefixo empilhado (T_TEX_Floor -> Floor)
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
