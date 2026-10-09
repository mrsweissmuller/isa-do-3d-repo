#if ISADO3D_MAYA
using System.IO;
using UnityEditor;

namespace IsaDo3D.EnvironmentArt
{
    public static class OpenInMaya
    {
        private const string MayaPathPrefKey = "Isa3D.OpenInMaya.MayaPath";
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

        [MenuItem("Isa do 3D/Maya/Configurar Caminho do Maya...")]
        public static void ConfigureMayaPath()
        {
            DccLauncher.ConfigurePath(Maya);
        }

        [MenuItem("Assets/Abrir no Maya", true)]
        [MenuItem("GameObject/Abrir no Maya", true)]
        private static bool ValidateOpen()
        {
            return DccLauncher.CanOpenSelection(Maya);
        }

        [MenuItem("Assets/Abrir no Maya")]
        [MenuItem("GameObject/Abrir no Maya", false, 0)]
        [MenuItem("Isa do 3D/Maya/Abrir Selecionado no Maya")]
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
#endif //ISADO3D_MAYA
