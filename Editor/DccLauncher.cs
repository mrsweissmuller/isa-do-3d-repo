using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace ArtTools.EnvironmentArt
{
    internal class DccDescriptor
    {
        public string ToolName;
        public string ExeFileName;
        public string DefaultRoot;
        public string PathPrefKey;
        public string[] ModelExtensions;

        public Func<string> AutoDetect;

        public Func<string, string> WriteOpenScript;

        public string ScriptPathArgumentFormat;

        public string LogPrefix => $"[{ToolName}]";
    }

    internal static class DccLauncher
    {
        public static bool CanOpenSelection(DccDescriptor dcc)
        {
            return MeshAssetResolver.TryResolveMeshAssetPath(Selection.activeObject, dcc.ModelExtensions, out _, out _);
        }

        public static void OpenSelectedMesh(DccDescriptor dcc)
        {
            if (!MeshAssetResolver.TryResolveMeshAssetPath(Selection.activeObject, dcc.ModelExtensions, out var meshAssetPath, out var sourceDescription))
            {
                EditorUtility.DisplayDialog(
                    $"Open in {dcc.ToolName}",
                    "Select a prefab, GameObject or mesh that has a mesh (MeshFilter/SkinnedMeshRenderer) on it.",
                    "OK");
                return;
            }

            var exePath = GetOrPromptPath(dcc);
            if (string.IsNullOrEmpty(exePath)) return;

            var absoluteMeshPath = AssetPaths.Absolute(meshAssetPath);
            if (!File.Exists(absoluteMeshPath))
            {
                Debug.LogError($"{dcc.LogPrefix} File not found on disk: {absoluteMeshPath}");
                return;
            }

            var scriptPath = dcc.WriteOpenScript(absoluteMeshPath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Format(dcc.ScriptPathArgumentFormat, scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                Process.Start(psi);
                Debug.Log($"{dcc.LogPrefix} Opening '{sourceDescription}' ({absoluteMeshPath}) in {dcc.ToolName}.");
            }
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException)
            {
                Debug.LogError($"{dcc.LogPrefix} Failed to start {dcc.ToolName} at '{exePath}': {e.Message}");
            }
        }

        public static string GetOrPromptPath(DccDescriptor dcc)
        {
            return ExternalToolLocator.GetOrPromptPath(
                dcc.PathPrefKey, dcc.LogPrefix, dcc.ToolName, dcc.ExeFileName, dcc.DefaultRoot, dcc.AutoDetect);
        }

        public static void ConfigurePath(DccDescriptor dcc)
        {
            ExternalToolLocator.ConfigurePath(
                dcc.PathPrefKey, dcc.LogPrefix, dcc.ToolName, dcc.ExeFileName, dcc.DefaultRoot);
        }
    }
}
