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
        public const string ComponentCopy = "COMPONENT_COPY";
        public const string HierarchyOrganizer = "HIERARCHY_ORGANIZER";
        public const string HierarchyHeaders = "HIERARCHY_HEADERS";
        public const string LodBuilder = "LOD_BUILDER";
        public const string MissingScripts = "MISSING_SCRIPTS";
        public const string Blender = "BLENDER";
        public const string Maya = "MAYA";
        public const string Krita = "KRITA";
        public const string Overlay = "OVERLAY";

        public static readonly string[] All =
        {
            ObjectEditor, MaterialBatch, ComponentCopy, HierarchyOrganizer, HierarchyHeaders,
            LodBuilder, MissingScripts, Blender, Maya, Krita, Overlay,
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

        internal static HashSet<string> Enabled() => new HashSet<string>(instance._enabled);

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
                    Debug.Log("[Isa do 3D] Nenhuma ferramenta ligada pra você neste projeto. " +
                              "Menu Isa do 3D/Ferramentas... pra escolher quais entram.");
                    return;
                }

                Debug.Log($"[Isa do 3D] {migrated.Length} ferramenta(s) que estavam ligadas por define agora ficam salvas só pra você, em UserSettings. " +
                          "Os defines ISADO3D_ saíram do ProjectSettings.asset, dá pra commitar essa limpeza.");
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
