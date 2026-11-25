using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class ToolIds
    {
        public const string ObjectEditor = "OBJECT_EDITOR";
        public const string MaterialBatch = "MATERIAL_BATCH";
        public const string HierarchyOrganizer = "HIERARCHY_ORGANIZER";
        public const string HierarchyHeaders = "HIERARCHY_HEADERS";
        public const string TextureMaterial = "TEXTURE_MATERIAL";
        public const string Blender = "BLENDER";
        public const string Maya = "MAYA";
        // O valor é do tempo em que só abria o Krita, mudar desligaria a ferramenta de quem já tinha ligado
        public const string ImageEditor = "KRITA";
        public const string Overlay = "OVERLAY";

        public static readonly string[] All =
        {
            ObjectEditor, MaterialBatch, HierarchyOrganizer, HierarchyHeaders,
            TextureMaterial, Blender, Maya, ImageEditor, Overlay,
        };
    }

    // Fica em UserSettings, que não vai pro Git, então cada um liga o que quiser
    [FilePath("UserSettings/IsaDo3DTools.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class IsaDo3DToolState : ScriptableSingleton<IsaDo3DToolState>
    {
        [SerializeField] private List<string> _enabled = new List<string>();
        [SerializeField] private bool _migrated;

        internal static event Action Changed;

        internal static bool IsOn(string tool) => instance._enabled.Contains(tool);

        // Id de ferramenta que já saiu do pacote continua no arquivo de quem tinha ela ligada
        internal static HashSet<string> Enabled() => new HashSet<string>(instance._enabled.Where(ToolIds.All.Contains));

        internal static void SetEnabled(IEnumerable<string> tools)
        {
            instance._enabled = tools.Distinct().OrderBy(t => t).ToList();
            instance.Save(true);

            Changed?.Invoke();
            EditorApplication.RepaintHierarchyWindow();

            // A Hierarchy nova só refaz o bind das linhas assim
            EditorApplication.DirtyHierarchyWindowSorting();
        }

        // Versão antiga ligava por define no ProjectSettings, que é igual pra todo mundo
        [InitializeOnLoadMethod]
        private static void MigrateFromDefines()
        {
            EditorApplication.delayCall += () =>
            {
                if (instance._migrated) return;
                instance._migrated = true;

                var migrated = RemoveOldDefines();

                // Define de ferramenta que já saiu do pacote não entra
                SetEnabled(migrated.Where(ToolIds.All.Contains));

                if (migrated.Length == 0)
                {
                    Debug.Log("[Art Tools] No tools are enabled for you in this project. " +
                              "Use Art Tools/Settings... to pick them.");
                    return;
                }

                Debug.Log($"[Art Tools] {migrated.Length} tool(s) that were enabled by scripting define are now saved just for you, in UserSettings. " +
                          "The ISADO3D_ defines were removed from ProjectSettings.asset, and that cleanup can be committed.");
            };
        }

        private static string[] RemoveOldDefines()
        {
            const string prefix = "ISADO3D_";
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown) return new string[0];

            var target = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            var old = defines.Where(d => d.StartsWith(prefix)).ToArray();
            if (old.Length == 0) return old;

            PlayerSettings.SetScriptingDefineSymbols(target, defines.Except(old).ToArray());
            return old.Select(d => d.Substring(prefix.Length)).ToArray();
        }
    }
}
