using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            bool confirmed = EditorUtility.DisplayDialog(
                "Configurar Batex",
                "Isso vai abrir o Blender em segundo plano, definir a pasta de exportação do addon Batex para:\n\n" +
                exportPath +
                "\n\ne salvar como arquivo de inicialização padrão do Blender (sobrescreve o startup.blend atual, " +
                "mas preserva o resto do que já está nele). Precisa que o addon Batex já esteja habilitado " +
                "no seu Blender. Continuar?",
                "Sim, configurar", "Cancelar");
            if (!confirmed) return;

            EditorPrefs.SetString(BatexExportFolderPrefKey, exportPath);

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
            // Em pacote do git ou file: o Packages/ não existe no disco
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OpenInBlender).Assembly);
            var addonAbsolutePath = package == null
                ? ""
                : Path.Combine(package.resolvedPath, "BlenderAddons", "isado3d_krita_bridge.py").Replace('\\', '/');

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

        [MenuItem("Isa do 3D/Blender/Configurar Caminho do Blender...", true)]
        [MenuItem("Isa do 3D/Blender/Configurar Pasta de Exportação do Batex...", true)]
        [MenuItem("Isa do 3D/Blender/Instalar Krita Bridge (addon)...", true)]
        [MenuItem("Isa do 3D/Blender/Abrir Selecionado no Blender", true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.Blender);

        [MenuItem("Assets/Abrir no Blender", true)]
        [MenuItem("GameObject/Abrir no Blender", true)]
        private static bool ValidateOpen()
        {
            return IsOn() && DccLauncher.CanOpenSelection(Blender);
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
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException)
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
                    // Pode ter saído entre o HasExited e o Kill
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception e)
                    {
                        Debug.LogError($"[Blender] Não consegui encerrar o Blender: {e.Message}");
                    }

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
                  $"{importCall}\n" +
                  TextureScript(absoluteMeshPath);

            return TempScriptFile.Write("open_in_blender", ".py", script);
        }

        // Quem sabe qual textura o modelo usa é a Unity: o importer guarda o material de cada slot do FBX,
        // e o material aponta pra textura. O Blender só recebe nome do material -> arquivo da textura.
        private const string TextureScriptTemplate = @"
import os
import re
TEXTURES = {__TEXTURES__}
ASSETS_ROOT = __ASSETS_ROOT__
IMAGE_EXTS = ('.png', '.jpg', '.jpeg', '.tga', '.bmp', '.tif', '.tiff', '.exr', '.hdr')
_index = None

def _find_texture(name):
    base = re.sub(r'\.\d{3}$', '', name)
    return TEXTURES.get(name) or TEXTURES.get(base)

def _apply_textures():
    applied = 0
    for mat in bpy.data.materials:
        path = _find_texture(mat.name)
        if not path:
            continue
        mat.use_nodes = True
        tree = mat.node_tree
        bsdf = next((n for n in tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            continue
        base_color = bsdf.inputs['Base Color']
        # O FBX costuma trazer o caminho da textura na maquina de quem exportou: o no existe, a imagem nao
        existing = base_color.links[0].from_node if base_color.is_linked else None
        if existing is not None and existing.type != 'TEX_IMAGE':
            continue
        try:
            image = bpy.data.images.load(path, check_existing=True)
        except RuntimeError as e:
            print('[IsaDo3D] Nao consegui carregar a textura ' + path + ': ' + str(e))
            continue
        if existing is not None:
            existing.image = image
        else:
            node = tree.nodes.new('ShaderNodeTexImage')
            node.image = image
            node.location = (bsdf.location.x - 400, bsdf.location.y)
            tree.links.new(node.outputs['Color'], base_color)
        applied += 1
        print('[IsaDo3D] Textura aplicada em ' + mat.name + ': ' + path)
    return applied

def _view3d_spaces():
    wm = bpy.context.window_manager
    screens = [w.screen for w in wm.windows] if wm else []
    for screen in screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                for space in area.spaces:
                    if space.type == 'VIEW_3D':
                        yield space

# Padrao do viewport: Solid, luz Flat, cor por Texture, contorno ligado e o resto desligado
def _apply_viewport_shading():
    for space in _view3d_spaces():
        shading = space.shading
        shading.type = 'SOLID'
        shading.light = 'FLAT'
        shading.color_type = 'TEXTURE'
        shading.wireframe_color_type = 'THEME'
        shading.background_type = 'THEME'
        shading.show_object_outline = True
        shading.object_outline_color = (0.0, 0.0, 0.0)
        shading.show_xray = False
        shading.show_shadows = False
        shading.use_dof = False
        shading.show_cavity = False
        shading.show_backface_culling = False

# Solid com cor Texture mostra o no de imagem ativo do material
def _activate_base_color_images():
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None or not bsdf.inputs['Base Color'].is_linked:
            continue
        source = bsdf.inputs['Base Color'].links[0].from_node
        if source.type == 'TEX_IMAGE':
            mat.node_tree.nodes.active = source

def _texture_index():
    global _index
    if _index is None:
        _index = {}
        for folder, _, files in os.walk(ASSETS_ROOT):
            for f in files:
                stem, ext = os.path.splitext(f)
                if ext.lower() in IMAGE_EXTS:
                    _index.setdefault(stem.lower(), []).append(os.path.join(folder, f))
    return _index

# Textura que o FBX aponta mas nao existe aqui (caminho da maquina de quem exportou): procura pelo nome em Assets
def _relink_missing_images():
    fixed = 0
    for image in bpy.data.images:
        if image.source != 'FILE' or image.packed_file or not image.filepath or image.users == 0:
            continue
        if os.path.exists(bpy.path.abspath(image.filepath)):
            continue
        stem = os.path.splitext(os.path.basename(image.filepath.replace('\\', '/')))[0].lower()
        found = _texture_index().get(stem)
        if not found:
            continue
        found.sort(key=lambda p: (not p.lower().endswith('.png'), p))
        image.filepath = found[0].replace('\\', '/')
        image.reload()
        fixed += 1
        print('[IsaDo3D] Textura reencontrada pelo nome: ' + found[0])
    return fixed

_apply_textures()
_relink_missing_images()
_activate_base_color_images()
try:
    _apply_viewport_shading()
except Exception as e:
    print('[IsaDo3D] Nao consegui ajustar o viewport: ' + str(e))
";

        private static string TextureScript(string absoluteMeshPath)
        {
            var textures = ResolveMaterialTextures(absoluteMeshPath);
            var entries = string.Join(", ", textures.Select(t => $"{PythonString(t.Key)}: {PythonString(t.Value)}"));
            var assetsRoot = Application.dataPath.Replace('\\', '/');
            return TextureScriptTemplate
                .Replace("__TEXTURES__", entries)
                .Replace("__ASSETS_ROOT__", PythonString(assetsRoot));
        }

        private static readonly string[] BlenderImageExtensions =
            { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff", ".exr", ".hdr" };

        // Nome do material no FBX -> caminho absoluto da textura, só dos que têm uma imagem que o Blender lê
        private static Dictionary<string, string> ResolveMaterialTextures(string absoluteMeshPath)
        {
            var result = new Dictionary<string, string>();

            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
            if (!absoluteMeshPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return result;
            var assetPath = absoluteMeshPath.Substring(projectRoot.Length);

            var materials = new Dictionary<string, Material>();
            if (AssetImporter.GetAtPath(assetPath) is ModelImporter importer)
            {
                foreach (var entry in importer.GetExternalObjectMap())
                    if (entry.Key.type == typeof(Material) && entry.Value is Material remapped)
                        materials[entry.Key.name] = remapped;
            }

            // Material embutido no FBX, sem remap pro projeto
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>())
                if (!materials.ContainsKey(embedded.name)) materials[embedded.name] = embedded;

            foreach (var entry in materials)
            {
                var texturePath = FindMainTexturePath(entry.Value);
                if (texturePath == null) continue;

                var ext = Path.GetExtension(texturePath).ToLowerInvariant();
                var absoluteTexture = AssetPaths.Absolute(texturePath);
                if (!BlenderImageExtensions.Contains(ext) || !File.Exists(absoluteTexture))
                {
                    Debug.LogWarning($"[Blender] Textura '{texturePath}' do material '{entry.Value.name}' não pode ser aberta no Blender (formato ou arquivo).");
                    continue;
                }
                result[entry.Key] = absoluteTexture;
            }

            return result;
        }

        private static string FindMainTexturePath(Material material)
        {
            if (material == null) return null;

            foreach (var property in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
            {
                if (!material.HasProperty(property)) continue;
                var path = AssetDatabase.GetAssetPath(material.GetTexture(property));
                if (!string.IsNullOrEmpty(path)) return path;
            }

            // Shader customizado com outro nome de propriedade: a primeira textura que o material tiver
            foreach (var property in material.GetTexturePropertyNames())
            {
                var path = AssetDatabase.GetAssetPath(material.GetTexture(property));
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return null;
        }

        private static string PythonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
