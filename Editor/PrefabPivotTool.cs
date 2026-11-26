using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using static ArtTools.EnvironmentArt.ArtToolsStyles;

namespace ArtTools.EnvironmentArt
{
    public class PrefabPivotTool : EditorWindow
    {
        internal const int AccentHex = 0x3D64C7;

        private enum PivotPreset { Center, Bottom, Top }

        private const float VertexSnapMaxScreenPixels = 40f;
        private const string PivotHolderTag = "ArtToolsPivotHolder";

        private Vector3 _customPivot;
        private bool _customModeActive;
        private bool _vertexSnapActive;
        private GameObject _lastSelected;
        private GameObject _surfaceTarget;

        private Label _status;
        private VisualElement _cards;
        private ObjectField _targetField;
        private Label _holderNote;
        private Button _revertButton;
        private Toggle _customToggle;
        private VisualElement _customFields;
        private Label _positionLabel;
        private Toggle _snapToggle;
        private Label _snapNote;
        private ObjectField _surfaceField;
        private Button _snapButton;

        [MenuItem("Art Tools/Object Editor", true)]
        private static bool IsOn() => ArtToolsState.IsOn(ToolIds.ObjectEditor);

        [MenuItem("Art Tools/Object Editor", false, 0)]
        public static void Open()
        {
            GetWindow<PrefabPivotTool>("Object Editor");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSelectionChange()
        {
            Refresh();
        }

        private void OnHierarchyChange()
        {
            Refresh();
        }

        private void CreateGUI()
        {
            minSize = new Vector2(320f, 300f);

            var page = Page(rootVisualElement);
            page.Add(Header("Object Editor"));

            _status = Note("");
            page.Add(_status);

            _cards = new VisualElement();
            _cards.Add(BuildTargetCard());
            _cards.Add(BuildPivotCard());
            _cards.Add(BuildSurfaceCard());
            page.Add(_cards);

            Refresh();
        }

        private VisualElement BuildTargetCard()
        {
            var card = Card("Selected object");

            _targetField = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = true };
            _targetField.SetEnabled(false);
            card.Add(_targetField);

            _holderNote = Note("");
            _holderNote.style.marginTop = 6f;
            card.Add(_holderNote);

            _revertButton = FlatButton("Revert pivot", false, () =>
            {
                var target = Selection.activeGameObject;
                var holder = target != null ? ResolvePivotHolder(target) : null;
                if (holder != null) RevertPivot(holder);
            });
            _revertButton.style.marginTop = 6f;
            card.Add(_revertButton);

            return card;
        }

        private VisualElement BuildPivotCard()
        {
            var card = Card("Edit pivot", "this instance only", out _);

            var automatic = Tile(Hex(AccentHex), out var automaticContent);
            automaticContent.Add(TileTitle("Automatic"));
            var presets = Row();
            presets.Add(PresetButton("Center", PivotPreset.Center, 4f));
            presets.Add(PresetButton("Bottom", PivotPreset.Bottom, 4f));
            presets.Add(PresetButton("Top", PivotPreset.Top, 0f));
            automaticContent.Add(presets);
            card.Add(automatic);

            var manual = Tile(Hex(AccentHex), out var manualContent);
            manual.style.marginBottom = 0f;
            manualContent.Add(TileTitle("Manual"));
            _customToggle = new Toggle("Enable editable mode");
            _customToggle.RegisterValueChangedCallback(e =>
            {
                _customModeActive = e.newValue;
                SceneView.RepaintAll();
                Refresh();
            });
            manualContent.Add(_customToggle);

            _customFields = new VisualElement();

            _positionLabel = new Label();
            _positionLabel.style.color = TextColor;
            _positionLabel.style.marginTop = 4f;
            _positionLabel.style.marginBottom = 4f;
            _customFields.Add(_positionLabel);

            _snapToggle = new Toggle("Snap to vertex");
            _snapToggle.RegisterValueChangedCallback(e =>
            {
                _vertexSnapActive = e.newValue;
                Refresh();
            });
            _customFields.Add(_snapToggle);

            _snapNote = Note("Ctrl+Click the model.");
            _customFields.Add(_snapNote);

            var apply = FlatButton("Apply", true, () =>
            {
                var target = Selection.activeGameObject;
                if (target == null) return;

                ApplyPivot(target, _customPivot);
                _customModeActive = false;
                SceneView.RepaintAll();
                Refresh();
            });
            apply.style.marginTop = 8f;
            _customFields.Add(apply);

            manualContent.Add(_customFields);
            card.Add(manual);
            return card;
        }

        private static Button PresetButton(string text, PivotPreset preset, float gap)
        {
            var button = FlatButton(text, false, () =>
            {
                var target = Selection.activeGameObject;
                if (target != null) ApplyPivot(target, PivotFromPreset(target, preset));
            });
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.marginRight = gap;
            return button;
        }

        private VisualElement BuildSurfaceCard()
        {
            var card = Card("Snap objects to a surface");

            var surfaceRow = Row();
            var setSurface = FlatButton("Set surface", false, () =>
            {
                _surfaceTarget = Selection.activeGameObject;
                Refresh();
            });
            setSurface.style.width = 120f;
            setSurface.style.marginRight = 4f;
            surfaceRow.Add(setSurface);

            _surfaceField = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = true };
            _surfaceField.style.flexGrow = 1f;
            _surfaceField.style.flexShrink = 1f;
            _surfaceField.RegisterValueChangedCallback(e =>
            {
                var newSurface = e.newValue as GameObject;
                if (newSurface != null && !newSurface.scene.IsValid())
                    Debug.LogWarning($"[Object Editor] '{newSurface.name}' is a Project asset, not a scene object. Drag an object from the Hierarchy.");
                else
                    _surfaceTarget = newSurface;
                Refresh();
            });
            surfaceRow.Add(_surfaceField);
            card.Add(surfaceRow);

            _snapButton = FlatButton("", true, SnapSelectedPivotsToSurface);
            _snapButton.style.marginTop = 8f;
            card.Add(_snapButton);

            return card;
        }

        private void Refresh()
        {
            if (_cards == null) return;

            var target = Selection.activeGameObject;
            if (target != _lastSelected)
            {
                _lastSelected = target;
                if (target != null) _customPivot = CalculateBounds(target).center;
            }

            bool inScene = target != null && target.scene.IsValid();
            SetVisible(_status, !inScene);
            SetVisible(_cards, inScene);
            if (!inScene)
            {
                _status.text = target == null
                    ? "Select an object in the scene."
                    : $"'{target.name}' is a Project asset. Open the prefab or select an instance in the scene.";
                return;
            }

            var existingHolder = ResolvePivotHolder(target);
            _targetField.SetValueWithoutNotify(target);
            SetVisible(_holderNote, existingHolder != null);
            SetVisible(_revertButton, existingHolder != null);
            if (existingHolder != null)
            {
                _holderNote.text = existingHolder == target
                    ? "This object is already a pivot."
                    : $"Inside the pivot '{existingHolder.name}'.";
            }

            _customToggle.SetValueWithoutNotify(_customModeActive);
            SetVisible(_customFields, _customModeActive);
            _snapToggle.SetValueWithoutNotify(_vertexSnapActive);
            SetVisible(_snapNote, _vertexSnapActive);
            RefreshPosition();

            _surfaceField.SetValueWithoutNotify(_surfaceTarget);
            _snapButton.text = $"Snap ({SnapCandidates().Length})";
            _snapButton.SetEnabled(_surfaceTarget != null);
        }

        private void RefreshPosition()
        {
            if (_positionLabel != null) _positionLabel.text = "Position: " + _customPivot.ToString("F3");
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_customModeActive) return;
            var target = Selection.activeGameObject;
            if (target == null) return;

            HandleVertexSnapClick(target);

            EditorGUI.BeginChangeCheck();
            var newPos = Handles.PositionHandle(_customPivot, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                _customPivot = newPos;
                RefreshPosition();
            }

            Handles.Label(_customPivot, "New pivot");
        }

        private void HandleVertexSnapClick(GameObject target)
        {
            if (!_vertexSnapActive) return;

            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !e.control) return;

            e.Use();
            if (!TryFindNearestVertex(target, e.mousePosition, out var worldVertex)) return;

            _customPivot = worldVertex;
            RefreshPosition();
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
            var meshes = renderers.Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            if (meshes.Length > 0) renderers = meshes;
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private GameObject[] SnapCandidates()
        {
            if (_surfaceTarget == null) return new GameObject[0];

            var surface = _surfaceTarget.transform;
            return Selection.GetFiltered<GameObject>(SelectionMode.TopLevel)
                .Where(g => g.scene.IsValid()
                            && g != _surfaceTarget
                            && !g.transform.IsChildOf(surface)
                            && !surface.IsChildOf(g.transform))
                .ToArray();
        }

        private void SnapSelectedPivotsToSurface()
        {
            if (_surfaceTarget == null) return;

            var toSnap = SnapCandidates();
            if (toSnap.Length == 0)
            {
                EditorUtility.DisplayDialog("Snap to Surface", "Select one or more objects to move.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Snap Objects to Surface");
            int undoGroup = Undo.GetCurrentGroup();

            float surfaceY = _surfaceTarget.transform.position.y;

            foreach (var obj in toSnap)
            {
                Undo.RecordObject(obj.transform, "Move object to surface");
                var current = obj.transform.position;
                obj.transform.position = new Vector3(current.x, surfaceY, current.z);
                EditorSceneManager.MarkSceneDirty(obj.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Object Editor] Moved {toSnap.Length} object(s) to the height of '{_surfaceTarget.name}' (Y={surfaceY:F3}).");
        }

        private static void ApplyPivot(GameObject target, Vector3 worldPivot)
        {
            var existingHolder = ResolvePivotHolder(target);

            string refusal = existingHolder == null ? RefusalToWrap(target) : null;
            if (refusal != null)
            {
                EditorUtility.DisplayDialog("Object Editor", refusal, "OK");
                return;
            }

            Undo.SetCurrentGroupName("Reposition Pivot");
            int undoGroup = Undo.GetCurrentGroup();

            GameObject pivotHolder = existingHolder != null
                ? RepositionExistingPivot(existingHolder, worldPivot)
                : CreateNewPivot(target, worldPivot);

            EditorSceneManager.MarkSceneDirty(pivotHolder.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = pivotHolder;
        }

        internal static bool IsPivotHolder(GameObject go)
        {
            string tag = go.tag;
            return tag == PivotHolderTag || tag == LegacyNames.PivotHolderTag;
        }

        private static GameObject ResolvePivotHolder(GameObject go)
        {
            if (IsPivotHolder(go)) return go;

            var parent = go.transform.parent;
            return parent != null && IsPivotHolder(parent.gameObject) ? parent.gameObject : null;
        }

        private static string RefusalToWrap(GameObject target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target) && !PrefabUtility.IsOutermostPrefabInstanceRoot(target))
                return $"'{target.name}' is inside a prefab. Select the instance root or edit the pivot inside the prefab.";

            if (target.name.StartsWith(HierarchyOrganizerTool.HeaderPrefix) || HierarchyOrganizerTool.IsTitle(target.name))
                return $"'{target.name}' is a Hierarchy Organizer group. Edit the pivot of the objects inside it.";

            if (target.transform.parent == null && HierarchyOrganizerTool.NeedsSceneRoot(target))
                return $"'{target.name}' uses DontDestroyOnLoad, which only works at the scene root. A new pivot would put it inside a parent.";

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
            Undo.SetCurrentGroupName("Revert Pivot");
            int undoGroup = Undo.GetCurrentGroup();

            var parent = pivotHolder.transform.parent;
            int siblingIndex = pivotHolder.transform.GetSiblingIndex();
            var scene = pivotHolder.scene;

            GameObject firstChild = null;
            while (pivotHolder.transform.childCount > 0)
            {
                var child = pivotHolder.transform.GetChild(0);
                if (firstChild == null) firstChild = child.gameObject;

                Undo.SetTransformParent(child, parent, "Detach from pivot");
                child.SetSiblingIndex(siblingIndex++);
            }

            string holderName = pivotHolder.name;
            Undo.DestroyObjectImmediate(pivotHolder);

            if (firstChild != null) Selection.activeGameObject = firstChild;
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[Object Editor] Reverted the pivot of '{holderName}': holder removed, object back on its original pivot.");
        }

        private static GameObject RepositionExistingPivot(GameObject pivotHolder, Vector3 worldPivot)
        {
            if (pivotHolder.transform.childCount == 0)
            {
                Debug.LogWarning($"[Object Editor] '{pivotHolder.name}' is empty, nothing to reposition.");
                return pivotHolder;
            }

            var children = new List<Transform>();
            foreach (Transform child in pivotHolder.transform) children.Add(child);

            foreach (var child in children)
                Undo.SetTransformParent(child, pivotHolder.transform.parent, "Detach from pivot temporarily");
            Undo.RecordObject(pivotHolder.transform, "Move existing pivot");
            pivotHolder.transform.position = worldPivot;
            foreach (var child in children)
                Undo.SetTransformParent(child, pivotHolder.transform, "Reattach to pivot");

            Debug.Log($"[Object Editor] Repositioned '{pivotHolder.name}' to {worldPivot:F3}.");
            return pivotHolder;
        }

        private static GameObject CreateNewPivot(GameObject target, Vector3 worldPivot)
        {
            var originalParent = target.transform.parent;
            int siblingIndex = target.transform.GetSiblingIndex();

            EnsureTagExists(PivotHolderTag);

            var pivotHolder = new GameObject(target.name + " [P]");
            Undo.RegisterCreatedObjectUndo(pivotHolder, "Create new pivot");
            pivotHolder.tag = PivotHolderTag;

            pivotHolder.transform.position = worldPivot;
            pivotHolder.transform.rotation = target.transform.rotation;

            Undo.SetTransformParent(pivotHolder.transform, originalParent, "Place new pivot in the hierarchy");
            pivotHolder.transform.SetSiblingIndex(siblingIndex);

            Undo.SetTransformParent(target.transform, pivotHolder.transform, "Move object to the new pivot");

            Debug.Log($"[Object Editor] Created a new pivot for '{pivotHolder.name}' at {worldPivot:F3}.");
            return pivotHolder;
        }
    }
}
