#if ISADO3D_BLENDER
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace IsaDo3D.EnvironmentArt
{
    public static class OpenInBlender
    {
        private const string BlenderPathPrefKey = "Isa3D.OpenInBlender.BlenderPath";
        private const string BatexExportFolderPrefKey = "Isa3D.OpenInBlender.BatexExportFolder";
        private const string BlenderRoot = "C:/Program Files/Blender Foundation";

        private static readonly DccDescriptor Blender = new DccDescriptor
        {
            ToolName = "Blender",
            ExeFileName = "blender.exe",
            DefaultRoot = BlenderRoot,
            PathPrefKey = BlenderPathPrefKey,
            // Sem .dae: o Blender 5.0 tirou o suporte a Collada
            ModelExtensions = new[] { ".fbx", ".obj", ".blend" },
            AutoDetect = () => ExternalToolLocator.FindNewestVersionedInstall(BlenderRoot, null, "blender.exe"),
            WriteOpenScript = WriteImportScript,
            ScriptPathArgumentFormat = "--python \"{0}\"",
        };

        [MenuItem("Isa do 3D/Blender/Configurar Caminho do Blender...")]
        public static void ConfigureBlenderPath()
        {
            DccLauncher.ConfigurePath(Blender);
        }

        [MenuItem("Isa do 3D/Blender/Configurar Pasta de Exportação do Batex...")]
        public static void ConfigureBatexExportFolder()
        {
            var current = EditorPrefs.GetString(BatexExportFolderPrefKey, "");
            var exportPath = EditorUtility.OpenFolderPanel("Selecione a pasta de exportação do Batex", string.IsNullOrEmpty(current) ? Application.dataPath : current, "");
            if (string.IsNullOrEmpty(exportPath)) return;

            EditorPrefs.SetString(BatexExportFolderPrefKey, exportPath);

            bool confirmed = EditorUtility.DisplayDialog(
                "Configurar Batex",
                "Isso vai abrir o Blender em segundo plano, definir a pasta de exportação do addon Batex para:\n\n" +
                exportPath +
                "\n\ne salvar como arquivo de inicialização padrão do Blender (sobrescreve o startup.blend atual, " +
                "mas preserva o resto do que já está nele). Precisa que o addon Batex já esteja habilitado " +
                "no seu Blender. Continuar?",
                "Sim, configurar", "Cancelar");
            if (!confirmed) return;

            var blenderPath = DccLauncher.GetOrPromptPath(Blender);
            if (string.IsNullOrEmpty(blenderPath)) return;

            var script =
                "import bpy\n" +
                $"target_path = r\"{exportPath}\"\n" +
                "if hasattr(bpy.context.scene, 'export_folder'):\n" +
                "    bpy.context.scene.export_folder = target_path\n" +
                "    bpy.ops.wm.save_homefile()\n" +
                "    print('[IsaDo3D] OK: export_folder configurado para ' + target_path + ' e salvo como startup padrao.')\n" +
                "else:\n" +
                "    print('[IsaDo3D] ERRO: propriedade export_folder nao existe nessa sessao. ' +\n" +
                "          'Habilite o addon Batex em Edit > Preferences > Add-ons e rode essa configuracao de novo.')\n";

            var scriptPath = TempScriptFile.Write("configure_batex", ".py", script);

            RunBackgroundScript(blenderPath, scriptPath, "Configurando Batex", (output, errorOutput) =>
            {
                if (!string.IsNullOrEmpty(output)) Debug.Log($"[Blender] Saída do Blender:\n{output}");
                if (!string.IsNullOrEmpty(errorOutput)) Debug.LogWarning($"[Blender] Blender stderr:\n{errorOutput}");

                if (output.Contains("[IsaDo3D] OK"))
                    Debug.Log("[Blender] Pasta de exportação do Batex configurada com sucesso.");
                else
                    Debug.LogWarning("[Blender] Não consegui confirmar sucesso pela saída, confira o log acima.");
            });
        }

        [MenuItem("Isa do 3D/Blender/Instalar Krita Bridge (addon)...")]
        public static void InstallKritaBridgeAddon()
        {
            const string addonRelativePath = "Packages/com.isado3d.tools/BlenderAddons/isado3d_krita_bridge.py";
            var addonAbsolutePath = Path.GetFullPath(addonRelativePath).Replace('\\', '/');

            if (!File.Exists(addonAbsolutePath))
            {
                Debug.LogError($"[Blender] Addon não encontrado em: {addonAbsolutePath}");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Instalar Krita Bridge",
                "Isso vai instalar/atualizar o addon 'Isa do 3D - Krita Bridge' no seu Blender e habilitá-lo " +
                "(sobrescreve uma versão anterior do mesmo addon se já existir, sem afetar outros addons). Continuar?",
                "Sim, instalar", "Cancelar");
            if (!confirmed) return;

            var blenderPath = DccLauncher.GetOrPromptPath(Blender);
            if (string.IsNullOrEmpty(blenderPath)) return;

            var script =
                "import bpy\n" +
                $"addon_path = r\"{addonAbsolutePath}\"\n" +
                "try:\n" +
                "    bpy.ops.preferences.addon_install(filepath=addon_path, overwrite=True)\n" +
                "    bpy.ops.preferences.addon_enable(module='isado3d_krita_bridge')\n" +
                "    bpy.ops.wm.save_userpref()\n" +
                "    print('[IsaDo3D] OK: addon Krita Bridge instalado e habilitado.')\n" +
                "except Exception as e:\n" +
                "    print('[IsaDo3D] ERRO: ' + str(e))\n";

            var scriptPath = TempScriptFile.Write("install_krita_bridge", ".py", script);

            RunBackgroundScript(blenderPath, scriptPath, "Instalando Krita Bridge", (output, errorOutput) =>
            {
                if (!string.IsNullOrEmpty(output)) Debug.Log($"[Blender] Saída do Blender:\n{output}");
                if (!string.IsNullOrEmpty(errorOutput)) Debug.LogWarning($"[Blender] Blender stderr:\n{errorOutput}");

                if (output.Contains("[IsaDo3D] OK"))
                    Debug.Log("[Blender] Addon Krita Bridge instalado com sucesso. Configure o caminho do Krita em Edit > Preferences > Add-ons > Isa do 3D - Krita Bridge se ele não detectar sozinho.");
                else
                    Debug.LogWarning("[Blender] Não consegui confirmar sucesso pela saída, confira o log acima.");
            });
        }

        [MenuItem("Assets/Abrir no Blender", true)]
        [MenuItem("GameObject/Abrir no Blender", true)]
        private static bool ValidateOpen()
        {
            return DccLauncher.CanOpenSelection(Blender);
        }

        [MenuItem("Assets/Abrir no Blender")]
        [MenuItem("GameObject/Abrir no Blender", false, 0)]
        [MenuItem("Isa do 3D/Blender/Abrir Selecionado no Blender")]
        public static void OpenSelected()
        {
            DccLauncher.OpenSelectedMesh(Blender);
        }

        // Polling pra não travar a Unity, ler o stdout síncrono antes do stderr dava deadlock
        private static void RunBackgroundScript(string blenderPath, string scriptPath, string progressTitle, Action<string, string> onComplete)
        {
            var psi = new ProcessStartInfo
            {
                FileName = blenderPath,
                Arguments = $"-b --python \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            Process process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Blender] Falha ao iniciar o Blender em '{blenderPath}': {e.Message}");
                return;
            }

            var output = new StringBuilder();
            var errorOutput = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorOutput.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var startTime = EditorApplication.timeSinceStartup;
            const double timeoutSeconds = 30;

            void Poll()
            {
                bool exited = process.HasExited;
                double elapsed = EditorApplication.timeSinceStartup - startTime;
                bool timedOut = elapsed > timeoutSeconds;
                bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                    progressTitle, "Aguardando o Blender em segundo plano...", (float)(elapsed / timeoutSeconds));

                if (!exited && !timedOut && !cancelled) return;

                EditorApplication.update -= Poll;
                EditorUtility.ClearProgressBar();

                if (!exited)
                {
                    try { process.Kill(); }
                    catch (Exception) { }

                    Debug.LogWarning(cancelled
                        ? "[Blender] Cancelado, o processo do Blender foi encerrado."
                        : "[Blender] O Blender não respondeu em 30s, processo encerrado.");
                }

                process.Dispose();

                TempScriptFile.TryDelete(scriptPath);

                onComplete(output.ToString(), errorOutput.ToString());
            }

            EditorApplication.update += Poll;
        }

        private static string WriteImportScript(string absoluteMeshPath)
        {
            var ext = Path.GetExtension(absoluteMeshPath).ToLowerInvariant();

            string importCall = ext switch
            {
                ".fbx" => $"bpy.ops.import_scene.fbx(filepath=r\"{absoluteMeshPath}\")",
                ".obj" => $"bpy.ops.wm.obj_import(filepath=r\"{absoluteMeshPath}\")",
                _ => null,
            };

            var script = ext == ".blend"
                ? $"import bpy\nbpy.ops.wm.open_mainfile(filepath=r\"{absoluteMeshPath}\")\n"
                : "import bpy\n" +
                  "for obj in list(bpy.data.objects):\n" +
                  "    bpy.data.objects.remove(obj, do_unlink=True)\n" +
                  $"{importCall}\n";

            return TempScriptFile.Write("open_in_blender", ".py", script);
        }
    }
}
#endif //ISADO3D_BLENDER
