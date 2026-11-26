using System.IO;
using UnityEditor;

namespace ArtTools.EnvironmentArt
{
    public static class OpenInMaya
    {
        internal const string MayaPathPrefKey = "ArtTools.OpenInMaya.MayaPath";
        private const string MayaRoot = "C:/Program Files/Autodesk";

        private static readonly DccDescriptor Maya = new DccDescriptor
        {
            ToolName = "Maya",
            ExeFileName = "maya.exe",
            DefaultRoot = MayaRoot,
            PathPrefKey = MayaPathPrefKey,
            ModelExtensions = new[] { ".fbx", ".obj", ".dae", ".ma", ".mb" },
            AutoDetect = () => ExternalToolLocator.FindNewestVersionedInstall(MayaRoot, "Maya", "bin", "maya.exe"),
            WriteOpenScript = WriteImportScript,
            ScriptPathArgumentFormat = "-script \"{0}\"",
        };

        internal static string SavedPath => EditorPrefs.GetString(MayaPathPrefKey, "");

        public static void ConfigureMayaPath()
        {
            DccLauncher.ConfigurePath(Maya);
        }

        private static bool IsOn() => ArtToolsState.IsOn(ToolIds.Maya);

        [MenuItem("Assets/Open in Maya", true)]
        [MenuItem("GameObject/Open in Maya", true)]
        private static bool ValidateOpen()
        {
            return IsOn() && DccLauncher.CanOpenSelection(Maya);
        }

        [MenuItem("Assets/Open in Maya")]
        [MenuItem("GameObject/Open in Maya", false, 0)]
        public static void OpenSelected()
        {
            DccLauncher.OpenSelectedMesh(Maya);
        }

        private static string WriteImportScript(string absoluteMeshPath)
        {
            var ext = Path.GetExtension(absoluteMeshPath).ToLowerInvariant();

            string script = ext switch
            {
                ".ma" or ".mb" =>
                    $"file -f -o \"{absoluteMeshPath}\";",
                ".obj" =>
                    "loadPlugin -quiet \"objExport\";\n" +
                    "file -f -new;\n" +
                    $"file -import -type \"OBJ\" -ignoreVersion \"{absoluteMeshPath}\";",
                _ =>
                    "loadPlugin -quiet \"fbxmaya\";\n" +
                    "file -f -new;\n" +
                    $"FBXImport -f \"{absoluteMeshPath}\";",
            };

            return TempScriptFile.Write("open_in_maya", ".mel", script);
        }
    }
}
