using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace IsaDo3D.EnvironmentArt
{
    [Overlay(typeof(SceneView), "Art Tools", true)]
    public class IsaDo3DToolbarOverlay : ToolbarOverlay
    {
        public IsaDo3DToolbarOverlay() : base(
            OpenInBlenderButton.Id,
            OpenInMayaButton.Id,
            OpenInImageEditorButton.Id,
            PivotToolButton.Id,
            MaterialBatchButton.Id,
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
            "Open the selected object's mesh in Blender", OpenInBlender.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInMayaButton : ToolButton
    {
        public const string Id = "IsaDo3D/OpenInMayaButton";

        public OpenInMayaButton() : base(ToolIds.Maya, "Maya",
            "Open the selected object's mesh in Maya", OpenInMaya.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInImageEditorButton : ToolButton
    {
        public const string Id = "IsaDo3D/OpenInImageEditorButton";

        public OpenInImageEditorButton() : base(ToolIds.ImageEditor, "Image",
            "Open the selected object's texture in the image editor", OpenInImageEditor.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class PivotToolButton : ToolButton
    {
        public const string Id = "IsaDo3D/PivotToolButton";

        public PivotToolButton() : base(ToolIds.ObjectEditor, "O.E.",
            "Open the Object Editor", PrefabPivotTool.Open) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class MaterialBatchButton : ToolButton
    {
        public const string Id = "IsaDo3D/MaterialBatchButton";

        public MaterialBatchButton() : base(ToolIds.MaterialBatch, "M.B.",
            "Open Material Batch (apply one material to the selected objects)", MaterialBatchTool.Open) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HierarchyOrganizerButton : ToolButton
    {
        public const string Id = "IsaDo3D/HierarchyOrganizerButton";

        public HierarchyOrganizerButton() : base(ToolIds.HierarchyOrganizer, "H.O.",
            "Open the Hierarchy Organizer (groups, auto organize and batch rename)",
            HierarchyOrganizerTool.Open) { }
    }
}
