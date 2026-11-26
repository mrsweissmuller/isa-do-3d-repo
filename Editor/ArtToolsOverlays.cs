using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace ArtTools.EnvironmentArt
{
    [Overlay(typeof(SceneView), "Art Tools", true)]
    public class ArtToolsToolbarOverlay : ToolbarOverlay
    {
        public ArtToolsToolbarOverlay() : base(
            OpenInBlenderButton.Id,
            OpenInMayaButton.Id,
            OpenInImageEditorButton.Id,
            PivotToolButton.Id,
            MaterialBatchButton.Id,
            HierarchyOrganizerButton.Id)
        {
        }

        private bool _wasOn;

        public override void OnCreated()
        {
            base.OnCreated();
            ArtToolsState.Changed += OnToolsChanged;
            _wasOn = ArtToolsState.IsOn(ToolIds.Overlay);
            if (!_wasOn) displayed = false;
        }

        public override void OnWillBeDestroyed()
        {
            ArtToolsState.Changed -= OnToolsChanged;
            base.OnWillBeDestroyed();
        }

        private void OnToolsChanged()
        {
            bool on = ArtToolsState.IsOn(ToolIds.Overlay);
            if (on == _wasOn) return;

            _wasOn = on;
            displayed = on;
        }
    }

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
                ArtToolsState.Changed += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => ArtToolsState.Changed -= Refresh);
        }

        private void Refresh()
        {
            style.display = ArtToolsState.IsOn(_tool) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInBlenderButton : ToolButton
    {
        public const string Id = "ArtTools/OpenInBlenderButton";

        public OpenInBlenderButton() : base(ToolIds.Blender, "Blender",
            "Open the selected object's mesh in Blender", OpenInBlender.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInMayaButton : ToolButton
    {
        public const string Id = "ArtTools/OpenInMayaButton";

        public OpenInMayaButton() : base(ToolIds.Maya, "Maya",
            "Open the selected object's mesh in Maya", OpenInMaya.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class OpenInImageEditorButton : ToolButton
    {
        public const string Id = "ArtTools/OpenInImageEditorButton";

        public OpenInImageEditorButton() : base(ToolIds.ImageEditor, "Image",
            "Open the selected object's texture in the image editor", OpenInImageEditor.OpenSelected) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class PivotToolButton : ToolButton
    {
        public const string Id = "ArtTools/PivotToolButton";

        public PivotToolButton() : base(ToolIds.ObjectEditor, "O.E.",
            "Open the Object Editor", PrefabPivotTool.Open) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class MaterialBatchButton : ToolButton
    {
        public const string Id = "ArtTools/MaterialBatchButton";

        public MaterialBatchButton() : base(ToolIds.MaterialBatch, "M.B.",
            "Open Material Batch (apply one material to the selected objects)", MaterialBatchTool.Open) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HierarchyOrganizerButton : ToolButton
    {
        public const string Id = "ArtTools/HierarchyOrganizerButton";

        public HierarchyOrganizerButton() : base(ToolIds.HierarchyOrganizer, "H.O.",
            "Open the Hierarchy Organizer (groups, auto organize and batch rename)",
            HierarchyOrganizerTool.Open) { }
    }
}
