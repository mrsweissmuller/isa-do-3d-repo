#if ISADO3D_OBJECT_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    public class PrefabPivotTool : EditorWindow
    {
        private enum PivotPreset { Center, Bottom, Top }

        private const float VertexSnapMaxScreenPixels = 40f;
        private const string PivotHolderTag = "IsaDo3DPivotHolder";

        private Vector3 _customPivot;
        private bool _customModeActive;
        private bool _vertexSnapActive;
        private GameObject _lastSelected;
        private GameObject _surfaceTarget;
        private Vector2 _scroll;

        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle SectionTitleStyle =>
            _sectionTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        [MenuItem("Isa do 3D/Cena/Object Editor")]
        public static void Open()
        {
            GetWindow<PrefabPivotTool>("Object Editor");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EnsureTagExists(PivotHolderTag);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            var target = Selection.activeGameObject;

            if (target != _lastSelected)
            {
                _lastSelected = target;
                if (target != null) _customPivot = CalculateBounds(target).center;
            }

            if (target == null)
            {
                EditorGUILayout.HelpBox("Selecione um objeto na cena (na Hierarchy ou na Scene View).", MessageType.Info);
                return;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                var existingHolder = ResolvePivotHolder(target);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Objeto selecionado", EditorStyles.boldLabel, GUILayout.Width(125));
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(target, typeof(GameObject), true);
                }
                if (existingHolder != null)
                {
                    EditorGUILayout.HelpBox(existingHolder == target
                        ? "Esse objeto já é um pivô, os botões abaixo vão reposicioná-lo."
                        : $"Esse objeto está dentro do pivô '{existingHolder.name}', os botões abaixo vão reposicionar esse pivô.",
                        MessageType.None);

                    if (GUILayout.Button("Reverter pivô (volta ao pivô original do modelo)"))
                    {
                        RevertPivot(existingHolder);
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Editar pivô", SectionTitleStyle);
                    EditorGUILayout.LabelField("Faz edições no pivô do objeto só nessa instância e nessa cena.", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(4);

                    EditorGUILayout.LabelField("Automático", EditorStyles.boldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Centro")) ApplyPivot(target, PivotFromPreset(target, PivotPreset.Center));
                        if (GUILayout.Button("Base")) ApplyPivot(target, PivotFromPreset(target, PivotPreset.Bottom));
                        if (GUILayout.Button("Topo")) ApplyPivot(target, PivotFromPreset(target, PivotPreset.Top));
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Manual", EditorStyles.boldLabel);
                    _customModeActive = EditorGUILayout.Toggle("Ativar modo editável", _customModeActive);
                    if (_customModeActive)
                    {
                        EditorGUILayout.LabelField("Posição: " + _customPivot.ToString("F3"));

                        _vertexSnapActive = EditorGUILayout.Toggle("Grudar no vértice", _vertexSnapActive);
                        if (_vertexSnapActive)
                            EditorGUILayout.HelpBox("Ctrl+Clique num ponto do modelo pra grudar no vértice mais próximo.", MessageType.None);

                        if (GUILayout.Button("Aplicar"))
                        {
                            ApplyPivot(target, _customPivot);
                            _customModeActive = false;
                            SceneView.RepaintAll();
                        }
                    }
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Encaixar objetos numa superfície", SectionTitleStyle);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(
                        "Define uma superfície de referência, seleciona os objetos, e move eles (X/Z mantido) " +
                        "até a altura do pivô da superfície.",
                        EditorStyles.wordWrappedMiniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Definir Superfície", GUILayout.Width(140)))
                            _surfaceTarget = target;
                        var newSurface = (GameObject)EditorGUILayout.ObjectField(_surfaceTarget, typeof(GameObject), true);
                        if (newSurface != _surfaceTarget)
                        {
                            if (newSurface != null && !newSurface.scene.IsValid())
                                Debug.LogWarning($"[Object Editor] '{newSurface.name}' é um asset do Project, não um objeto da cena. Arraste um objeto da Hierarchy.");
                            else
                                _surfaceTarget = newSurface;
                        }
                    }

                    using (new EditorGUI.DisabledScope(_surfaceTarget == null))
                    {
                        if (GUILayout.Button($"Encaixar ({Selection.gameObjects.Length})"))
                            SnapSelectedPivotsToSurface();
                    }
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_customModeActive) return;
            var target = Selection.activeGameObject;
            if (target == null) return;

            if (_vertexSnapActive)
            {
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && e.control)
                {
                    if (TryFindNearestVertex(target, e.mousePosition, out var worldVertex))
                    {
                        _customPivot = worldVertex;
                        Repaint();
                    }
                    e.Use();
                }
            }

            EditorGUI.BeginChangeCheck();
            var newPos = Handles.PositionHandle(_customPivot, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                _customPivot = newPos;
                Repaint();
            }

            Handles.Label(_customPivot, "Novo pivô");
        }

        private static bool TryFindNearestVertex(GameObject target, Vector2 mouseScreenPos, out Vector3 worldVertex)
        {
            worldVertex = Vector3.zero;
            float bestDistSqr = VertexSnapMaxScreenPixels * VertexSnapMaxScreenPixels;
            bool found = false;

            foreach (var mf in target.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                CheckVertices(mf.sharedMesh.vertices, mf.transform.localToWorldMatrix, mouseScreenPos, ref bestDistSqr, ref worldVertex, ref found);
            }

            foreach (var smr in target.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                CheckVertices(baked.vertices, smr.transform.localToWorldMatrix, mouseScreenPos, ref bestDistSqr, ref worldVertex, ref found);
                Object.DestroyImmediate(baked);
            }

            return found;
        }

        private static void CheckVertices(Vector3[] localVerts, Matrix4x4 localToWorld, Vector2 mouseScreenPos, ref float bestDistSqr, ref Vector3 bestWorld, ref bool found)
        {
            foreach (var v in localVerts)
            {
                var world = localToWorld.MultiplyPoint3x4(v);
                var screen = HandleUtility.WorldToGUIPoint(world);
                float distSqr = (screen - mouseScreenPos).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    bestWorld = world;
                    found = true;
                }
            }
        }

        private static Vector3 PivotFromPreset(GameObject target, PivotPreset preset)
        {
            var bounds = CalculateBounds(target);
            return preset switch
            {
                PivotPreset.Center => bounds.center,
                PivotPreset.Bottom => new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                PivotPreset.Top => new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                _ => bounds.center,
            };
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private void SnapSelectedPivotsToSurface()
        {
            if (_surfaceTarget == null) return;

            var toSnap = Selection.gameObjects
                .Where(g => g != _surfaceTarget && !g.transform.IsChildOf(_surfaceTarget.transform))
                .ToArray();

            if (toSnap.Length == 0)
            {
                EditorUtility.DisplayDialog("Encaixar na Superfície", "Selecione um ou mais objetos pra mover.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Encaixar Objetos na Superfície");
            int undoGroup = Undo.GetCurrentGroup();

            float surfaceY = _surfaceTarget.transform.position.y;

            foreach (var obj in toSnap)
            {
                Undo.RecordObject(obj.transform, "Mover objeto para a superfície");
                var current = obj.transform.position;
                obj.transform.position = new Vector3(current.x, surfaceY, current.z);
                EditorSceneManager.MarkSceneDirty(obj.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Object Editor] {toSnap.Length} objeto(s) movido(s) pra altura de '{_surfaceTarget.name}' (Y={surfaceY:F3}).");
        }

        private static void ApplyPivot(GameObject target, Vector3 worldPivot)
        {
            var existingHolder = ResolvePivotHolder(target);

            // A Unity não deixa mudar a hierarquia dentro de uma instância de prefab
            if (existingHolder == null
                && PrefabUtility.IsPartOfPrefabInstance(target)
                && !PrefabUtility.IsOutermostPrefabInstanceRoot(target))
            {
                EditorUtility.DisplayDialog("Object Editor",
                    $"'{target.name}' está dentro de um prefab. Selecione a raiz da instância ou edite o pivô dentro do prefab.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Reposicionar Pivô");
            int undoGroup = Undo.GetCurrentGroup();

            GameObject result = existingHolder != null
                ? RepositionExistingPivot(existingHolder, worldPivot)
                : CreateNewPivot(target, worldPivot);

            EditorSceneManager.MarkSceneDirty(result.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = result;
        }

        // Tag fora do laço, o InternalEditorUtility.tags cria um array a cada acesso
        private static GameObject ResolvePivotHolder(GameObject go)
        {
            if (!InternalEditorUtility.tags.Contains(PivotHolderTag)) return null;

            for (var t = go.transform; t != null; t = t.parent)
                if (t.gameObject.CompareTag(PivotHolderTag)) return t.gameObject;
            return null;
        }

        private static void EnsureTagExists(string tag)
        {
            if (InternalEditorUtility.tags.Contains(tag)) return;

            InternalEditorUtility.AddTag(tag);
            AssetDatabase.SaveAssets();
        }

        private static void RevertPivot(GameObject pivotHolder)
        {
            Undo.SetCurrentGroupName("Reverter Pivô");
            int undoGroup = Undo.GetCurrentGroup();

            var parent = pivotHolder.transform.parent;
            int siblingIndex = pivotHolder.transform.GetSiblingIndex();
            var scene = pivotHolder.scene;

            GameObject firstChild = null;
            while (pivotHolder.transform.childCount > 0)
            {
                var child = pivotHolder.transform.GetChild(0);
                if (firstChild == null) firstChild = child.gameObject;

                Undo.SetTransformParent(child, parent, "Destacar do pivô");
                child.SetSiblingIndex(siblingIndex++);
            }

            string holderName = pivotHolder.name;
            Undo.DestroyObjectImmediate(pivotHolder);

            if (firstChild != null) Selection.activeGameObject = firstChild;
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[Object Editor] Pivô de '{holderName}' revertido: holder removido, objeto de volta ao pivô original.");
        }

        private static GameObject RepositionExistingPivot(GameObject pivotHolder, Vector3 worldPivot)
        {
            if (pivotHolder.transform.childCount == 0)
            {
                Debug.LogWarning($"[Object Editor] '{pivotHolder.name}' está vazio, nada a reposicionar.");
                return pivotHolder;
            }

            var meshChild = pivotHolder.transform.GetChild(0);

            Undo.SetTransformParent(meshChild, pivotHolder.transform.parent, "Destacar temporariamente do pivô");
            Undo.RecordObject(pivotHolder.transform, "Mover pivô existente");
            pivotHolder.transform.position = worldPivot;
            Undo.SetTransformParent(meshChild, pivotHolder.transform, "Reencaixar no pivô");

            Debug.Log($"[Object Editor] '{pivotHolder.name}' reposicionado para {worldPivot:F3}.");
            return pivotHolder;
        }

        private static GameObject CreateNewPivot(GameObject target, Vector3 worldPivot)
        {
            var originalParent = target.transform.parent;
            int siblingIndex = target.transform.GetSiblingIndex();

            EnsureTagExists(PivotHolderTag);

            var pivotHolder = new GameObject(target.name + " [P]");
            Undo.RegisterCreatedObjectUndo(pivotHolder, "Criar novo pivô");
            pivotHolder.tag = PivotHolderTag;

            pivotHolder.transform.position = worldPivot;
            pivotHolder.transform.rotation = target.transform.rotation;

            Undo.SetTransformParent(pivotHolder.transform, originalParent, "Posicionar novo pivô na hierarquia");
            pivotHolder.transform.SetSiblingIndex(siblingIndex);

            Undo.SetTransformParent(target.transform, pivotHolder.transform, "Mover objeto para o novo pivô");

            Debug.Log($"[Object Editor] Novo pivô criado para '{pivotHolder.name}' em {worldPivot:F3}.");
            return pivotHolder;
        }
    }
}
#endif //ISADO3D_OBJECT_EDITOR
