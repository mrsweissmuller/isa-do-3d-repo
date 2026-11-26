using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ArtTools.EnvironmentArt
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
        public const string ImageEditor = "KRITA";
        public const string Overlay = "OVERLAY";
        public const string FolderColors = "FOLDER_COLORS";

        public static readonly string[] All =
        {
            ObjectEditor, MaterialBatch, HierarchyOrganizer, HierarchyHeaders,
            TextureMaterial, Blender, Maya, ImageEditor, Overlay, FolderColors,
        };
    }

    [FilePath(ArtToolsState.SavedAt, FilePathAttribute.Location.ProjectFolder)]
    internal class ArtToolsState : ScriptableSingleton<ArtToolsState>
    {
        internal const string SavedAt = "UserSettings/ArtTools.asset";

        [SerializeField] private List<string> _enabled = new List<string>();
        [SerializeField] private bool _migrated;

        internal static event Action Changed;

        internal static bool IsOn(string tool) => instance._enabled.Contains(tool);

        internal static HashSet<string> Enabled() => new HashSet<string>(instance._enabled.Where(ToolIds.All.Contains));

        internal static void SetEnabled(IEnumerable<string> tools)
        {
            instance._enabled = tools.Distinct().OrderBy(t => t).ToList();
            instance.Save(true);

            Changed?.Invoke();
            EditorApplication.RepaintHierarchyWindow();

            EditorApplication.DirtyHierarchyWindowSorting();
        }

        [InitializeOnLoadMethod]
        private static void MigrateFromDefines()
        {
            EditorApplication.delayCall += () =>
            {
                if (instance._migrated) return;
                instance._migrated = true;

                var migrated = RemoveOldDefines();

                SetEnabled(migrated.Where(ToolIds.All.Contains));

                if (migrated.Length == 0)
                {
                    Debug.Log("[Art Tools] No tools are enabled for you in this project. " +
                              "Use Art Tools/Settings... to pick them.");
                    return;
                }

                Debug.Log($"[Art Tools] {migrated.Length} tool(s) that were enabled by scripting define are now saved just for you, in UserSettings. " +
                          $"The {LegacyNames.DefinePrefix} defines were removed from ProjectSettings.asset, and that cleanup can be committed.");
            };
        }

        private static string[] RemoveOldDefines()
        {
            const string prefix = LegacyNames.DefinePrefix;
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

    internal static class LegacyNames
    {
        internal const string DefinePrefix = "ISADO3D_";
        internal const string PivotHolderTag = "IsaDo3DPivotHolder";
        internal const string KritaPathPref = "Isa3D.OpenInKrita.KritaPath";

        private static readonly (string Old, string Renamed)[] Files =
        {
            ("UserSettings/IsaDo3DTools.asset", ArtToolsState.SavedAt),
            ("UserSettings/IsaDo3DFolderColors.asset", FolderColorState.SavedAt),

            ("ProjectSettings/ArtToolsHierarchyColors.json", HierarchyGroupColors.FilePath),
            ("ProjectSettings/IsaDo3DHierarchyColors.json", HierarchyGroupColors.FilePath),
        };

        private static readonly (string Old, string Renamed)[] TextPrefs =
        {
            ("Isa3D.OpenInBlender.BlenderPath", OpenInBlender.BlenderPathPrefKey),
            ("Isa3D.OpenInBlender.BatexExportFolder", OpenInBlender.BatexExportFolderPrefKey),
            ("Isa3D.OpenInMaya.MayaPath", OpenInMaya.MayaPathPrefKey),
        };

        private static readonly (string Old, string Renamed)[] TogglePrefs =
        {
            ("IsaDo3D.Hierarchy.ChildTint", HierarchyViewPrefs.TintKey),
            ("IsaDo3D.Hierarchy.TreeLines", HierarchyViewPrefs.LinesKey),
        };

        private const string OldTintAlphaKey = "IsaDo3D.Hierarchy.ChildTintAlpha";

        [InitializeOnLoadMethod]
        private static void MoveToNewNames()
        {
            foreach (var (old, renamed) in Files)
            {
                if (!File.Exists(old) || File.Exists(renamed)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(renamed));
                File.Move(old, renamed);
            }

            foreach (var (old, renamed) in TextPrefs)
                if (EditorPrefs.HasKey(old) && !EditorPrefs.HasKey(renamed))
                    EditorPrefs.SetString(renamed, EditorPrefs.GetString(old));

            foreach (var (old, renamed) in TogglePrefs)
                if (EditorPrefs.HasKey(old) && !EditorPrefs.HasKey(renamed))
                    EditorPrefs.SetBool(renamed, EditorPrefs.GetBool(old));

            if (EditorPrefs.HasKey(OldTintAlphaKey) && !EditorPrefs.HasKey(HierarchyViewPrefs.TintAlphaKey))
                EditorPrefs.SetFloat(HierarchyViewPrefs.TintAlphaKey, EditorPrefs.GetFloat(OldTintAlphaKey));
        }
    }
}
