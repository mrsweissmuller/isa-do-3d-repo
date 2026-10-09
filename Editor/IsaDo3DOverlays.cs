#if ISADO3D_OVERLAY && (ISADO3D_BLENDER || ISADO3D_MAYA || ISADO3D_KRITA || ISADO3D_OBJECT_EDITOR || ISADO3D_HIERARCHY_ORGANIZER)
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;

namespace IsaDo3D.EnvironmentArt
{
    [Overlay(typeof(SceneView), "Isa do 3D", true)]
    public class IsaDo3DToolbarOverlay : ToolbarOverlay
    {
        public IsaDo3DToolbarOverlay() : base(ButtonIds()) { }

        // Ferramenta desligada não pode entrar, o botão chama ela direto
        private static string[] ButtonIds()
        {
            var ids = new List<string>();
#if ISADO3D_BLENDER
            ids.Add(OpenInBlenderButton.Id);
#endif
#if ISADO3D_MAYA
            ids.Add(OpenInMayaButton.Id);
#endif
#if ISADO3D_KRITA
            ids.Add(OpenInKritaButton.Id);
#endif
#if ISADO3D_OBJECT_EDITOR
            ids.Add(PivotToolButton.Id);
#endif
#if ISADO3D_HIERARCHY_ORGANIZER
            ids.Add(HierarchyOrganizerButton.Id);
#endif
            return ids.ToArray();
        }
    }

#if ISADO3D_BLENDER
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInBlenderButton : EditorToolbarButton
    {
        public const string Id = "IsaDo3D/OpenInBlenderButton";

        public OpenInBlenderButton()
        {
            text = "Blender";
            tooltip = "Abrir a mesh do objeto selecionado no Blender";
            clicked += OpenInBlender.OpenSelected;
        }
    }
#endif

#if ISADO3D_MAYA
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInMayaButton : EditorToolbarButton
    {
        public const string Id = "IsaDo3D/OpenInMayaButton";

        public OpenInMayaButton()
        {
            text = "Maya";
            tooltip = "Abrir a mesh do objeto selecionado no Maya";
            clicked += OpenInMaya.OpenSelected;
        }
    }
#endif

#if ISADO3D_KRITA
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInKritaButton : EditorToolbarButton
    {
        public const string Id = "IsaDo3D/OpenInKritaButton";

        public OpenInKritaButton()
        {
            text = "Krita";
            tooltip = "Abrir a textura do objeto selecionado no Krita";
            clicked += OpenInKrita.OpenSelected;
        }
    }
#endif

#if ISADO3D_OBJECT_EDITOR
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class PivotToolButton : EditorToolbarButton
    {
        public const string Id = "IsaDo3D/PivotToolButton";

        public PivotToolButton()
        {
            text = "Object Editor";
            tooltip = "Abrir o Object Editor";
            clicked += PrefabPivotTool.Open;
        }
    }
#endif

#if ISADO3D_HIERARCHY_ORGANIZER
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HierarchyOrganizerButton : EditorToolbarButton
    {
        public const string Id = "IsaDo3D/HierarchyOrganizerButton";

        public HierarchyOrganizerButton()
        {
            text = "Hierarchy Organizer";
            tooltip = "Abrir o Hierarchy Organizer (grupos, organização automática e renomear em lote)";
            clicked += HierarchyOrganizerTool.Open;
        }
    }
#endif
}
#endif //ISADO3D_OVERLAY && ...
