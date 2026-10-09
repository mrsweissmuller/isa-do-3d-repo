using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace IsaDo3D.EnvironmentArt
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
                    $"Abrir no {dcc.ToolName}",
                    "Selecione um prefab, GameObject ou mesh que tenha uma malha (MeshFilter/SkinnedMeshRenderer) associada.",
                    "OK");
                return;
            }

            var exePath = GetOrPromptPath(dcc);
            if (string.IsNullOrEmpty(exePath)) return;

            var absoluteMeshPath = Path.GetFullPath(meshAssetPath).Replace('\\', '/');
            if (!File.Exists(absoluteMeshPath))
            {
                Debug.LogError($"{dcc.LogPrefix} Arquivo não encontrado no disco: {absoluteMeshPath}");
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
                Debug.Log($"{dcc.LogPrefix} Abrindo '{sourceDescription}' ({absoluteMeshPath}) no {dcc.ToolName}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"{dcc.LogPrefix} Falha ao iniciar o {dcc.ToolName} em '{exePath}': {e.Message}");
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
