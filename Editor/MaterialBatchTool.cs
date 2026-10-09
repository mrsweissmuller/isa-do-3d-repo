#if ISADO3D_MATERIAL_BATCH
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    public class MaterialBatchTool : EditorWindow
    {
        private Material _material;
        private int _slotIndex;
        private bool _allSlots;

        private static GUIStyle _titleStyle;
        private static GUIStyle TitleStyle =>
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        [MenuItem("Isa do 3D/Cena/Material Batch")]
        public static void Open()
        {
            GetWindow<MaterialBatchTool>("Material Batch");
        }

        // Por causa da contagem no botão
        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Material Batch", TitleStyle);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Aplica um material a todos os objetos selecionados (e filhos com MeshRenderer/" +
                    "SkinnedMeshRenderer, inclusive os desativados). Usa sharedMaterial, então não " +
                    "cria cópias/instâncias.",
                    EditorStyles.wordWrappedMiniLabel);

                _material = (Material)EditorGUILayout.ObjectField("Material", _material, typeof(Material), false);

                _allSlots = EditorGUILayout.Toggle("Aplicar em todos os slots", _allSlots);
                using (new EditorGUI.DisabledScope(_allSlots))
                {
                    _slotIndex = Mathf.Max(0, EditorGUILayout.IntField("Slot (índice)", _slotIndex));
                }

                var selected = Selection.gameObjects;

                using (new EditorGUI.DisabledScope(_material == null || selected.Length == 0))
                {
                    if (GUILayout.Button($"Aplicar aos Selecionados ({selected.Length})"))
                        ApplyToSelection(selected);
                }
            }
        }

        private void ApplyToSelection(GameObject[] selected)
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

            var summary = $"[Material Batch] Material '{_material.name}' aplicado em {changed} renderer(s).";
            if (skippedNoSlot > 0) summary += $" {skippedNoSlot} renderer(s) pulado(s) por não ter o slot {_slotIndex}.";
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

            Undo.RecordObject(renderer, "Trocar material");
            renderer.sharedMaterials = mats;
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            changed++;
        }
    }
}
#endif //ISADO3D_MATERIAL_BATCH
