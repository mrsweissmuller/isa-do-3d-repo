using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace IsaDo3D.EnvironmentArt
{
    [Overlay(typeof(SceneView), "Isa do 3D", true)]
    public class IsaDo3DToolbarOverlay : ToolbarOverlay
    {
        public IsaDo3DToolbarOverlay() : base(
            OpenInBlenderButton.Id,
            OpenInMayaButton.Id,
            OpenInKritaButton.Id,
            PivotToolButton.Id,
            HierarchyOrganizerButton.Id)
        {
        }

        private bool _wasOn;

        // Só mexe quando o toggle da barra muda, senão desfaz quem fechou ela pelo menu da Scene View
        public override void OnCreated()
        {
            base.OnCreated();
            IsaDo3DToolState.Changed += OnToolsChanged;
            _wasOn = IsaDo3DToolState.IsOn(ToolIds.Overlay);
            if (!_wasOn) displayed = false;
        }

        public override void OnWillBeDestroyed()
        {
            IsaDo3DToolState.Changed -= OnToolsChanged;
            base.OnWillBeDestroyed();
        }

        private void OnToolsChanged()
        {
            bool on = IsaDo3DToolState.IsOn(ToolIds.Overlay);
            if (on == _wasOn) return;

            _wasOn = on;
            displayed = on;
        }
    }

    // Ferramenta desligada some da barra
    internal abstract class ToolButton : EditorToolbarButton
    {
        private readonly string _tool;

        protected ToolButton(string tool, string label, string tip, Action onClick)
        {
            _tool = tool;
            text = label;
            tooltip = tip;
            clicked += onClick;

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                IsaDo3DToolState.Changed += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => IsaDo3DToolState.Changed -= Refresh);
        }

        private void Refresh()
        {
            style.display = IsaDo3DToolState.IsOn(_tool) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInBlenderButton : ToolButton
    {
        public const string Id = "IsaDo3D/OpenInBlenderButton";

        public OpenInBlenderButton() : base(ToolIds.Blender, "Blender",
            "Abrir a mesh do objeto selecionado no Blender", OpenInBlender.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInMayaButton : ToolButton
    {
        public const string Id = "IsaDo3D/OpenInMayaButton";

        public OpenInMayaButton() : base(ToolIds.Maya, "Maya",
            "Abrir a mesh do objeto selecionado no Maya", OpenInMaya.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInKritaButton : ToolButton
    {
        public const string Id = "IsaDo3D/OpenInKritaButton";

        public OpenInKritaButton() : base(ToolIds.Krita, "Krita",
            "Abrir a textura do objeto selecionado no Krita", OpenInKrita.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class PivotToolButton : ToolButton
    {
        public const string Id = "IsaDo3D/PivotToolButton";

        public PivotToolButton() : base(ToolIds.ObjectEditor, "O.E.",
            "Abrir o Object Editor", PrefabPivotTool.Open) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HierarchyOrganizerButton : ToolButton
    {
        public const string Id = "IsaDo3D/HierarchyOrganizerButton";

        public HierarchyOrganizerButton() : base(ToolIds.HierarchyOrganizer, "H.O.",
            "Abrir o Hierarchy Organizer (grupos, organização automática e renomear em lote)",
            HierarchyOrganizerTool.Open) { }
    }
}
