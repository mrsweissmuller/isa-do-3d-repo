using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static IsaDo3D.EnvironmentArt.IsaDo3DStyles;

namespace IsaDo3D.EnvironmentArt
{
    public class MaterialBatchTool : EditorWindow
    {
        private Material _material;
        private int _slotIndex;
        private bool _allSlots;

        // Asset do Project ficaria com o material trocado sem ninguém ver
        private static readonly List<GameObject> SceneSelection = new List<GameObject>();

        private static List<GameObject> SelectedInScene()
        {
            SceneSelection.Clear();
            foreach (var go in Selection.gameObjects)
                if (go.scene.IsValid()) SceneSelection.Add(go);
            return SceneSelection;
        }

        [MenuItem("Art Tools/Material Batch", true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.MaterialBatch);

        [MenuItem("Art Tools/Material Batch", false, 1)]
        public static void Open()
        {
            GetWindow<MaterialBatchTool>("Material Batch");
        }

        private IntegerField _slotField;
        private Button _applyButton;

        // Por causa da contagem no botão
        private void OnSelectionChange()
        {
            Refresh();
        }

        private void CreateGUI()
        {
            minSize = new Vector2(300f, 190f);

            var page = Page(rootVisualElement);
            page.Add(Header("Material Batch"));

            var card = Card("Material");

            var material = new ObjectField("Material")
            {
                objectType = typeof(Material),
                allowSceneObjects = false,
                value = _material,
            };
            material.RegisterValueChangedCallback(e =>
            {
                _material = e.newValue as Material;
                Refresh();
            });
            card.Add(material);

            var allSlots = new Toggle("Apply to all slots") { value = _allSlots };
            allSlots.RegisterValueChangedCallback(e =>
            {
                _allSlots = e.newValue;
                Refresh();
            });
            card.Add(allSlots);

            _slotField = new IntegerField("Slot (index)") { value = _slotIndex };
            _slotField.RegisterValueChangedCallback(e =>
            {
                _slotIndex = Mathf.Max(0, e.newValue);
                _slotField.SetValueWithoutNotify(_slotIndex);
            });
            card.Add(_slotField);

            _applyButton = FlatButton("", true, () => ApplyToSelection(SelectedInScene()));
            _applyButton.style.marginTop = 8f;
            card.Add(_applyButton);

            page.Add(card);
            Refresh();
        }

        private void Refresh()
        {
            if (_applyButton == null) return;

            int selected = SelectedInScene().Count;
            _slotField.SetEnabled(!_allSlots);
            _applyButton.text = $"Apply to selected ({selected})";
            _applyButton.SetEnabled(_material != null && selected > 0);
        }

        private void ApplyToSelection(List<GameObject> selected)
        {
            Undo.SetCurrentGroupName("Material Batch");
            int undoGroup = Undo.GetCurrentGroup();

            int changed = 0, skippedNoSlot = 0;

            foreach (var go in selected)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

                    ApplyToRenderer(renderer, ref changed, ref skippedNoSlot);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            var summary = $"[Material Batch] Applied material '{_material.name}' to {changed} renderer(s).";
            if (skippedNoSlot > 0) summary += $" Skipped {skippedNoSlot} renderer(s) with no slot {_slotIndex}.";
            Debug.Log(summary);
        }

        private void ApplyToRenderer(Renderer renderer, ref int changed, ref int skippedNoSlot)
        {
            var mats = renderer.sharedMaterials;
            if (mats.Length == 0) return;

            if (_allSlots)
            {
                for (int i = 0; i < mats.Length; ++i) mats[i] = _material;
            }
            else
            {
                if (_slotIndex >= mats.Length)
                {
                    skippedNoSlot++;
                    return;
                }
                mats[_slotIndex] = _material;
            }

            Undo.RecordObject(renderer, "Change material");
            renderer.sharedMaterials = mats;
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            changed++;
        }
    }
}
