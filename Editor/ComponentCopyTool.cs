using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    public class ComponentCopyTool : EditorWindow
    {
        // SerializeField pra não zerar quando os scripts recompilam
        [SerializeField] private GameObject _source;
        [SerializeField] private bool _includeChildren;
        [SerializeField] private bool _overwriteExisting;
        [SerializeField] private bool _adaptColliders = true;
        [SerializeField] private bool _remapReferences;
        [SerializeField] private bool _showObjectProperties;
        [SerializeField] private bool _copyLayer;
        [SerializeField] private bool _copyTag;
        [SerializeField] private bool _copyStatic;

        private class Stats
        {
            public int Copied, Updated, Skipped, Failed, MissingChild, Adapted, Unfitted, Layers, Tags, Statics, Remapped, Unmapped;
        }

        [MenuItem("Isa do 3D/Cena/Copiar Componentes", true)]
        private static bool IsOn() => IsaDo3DToolState.IsOn(ToolIds.ComponentCopy);

        [MenuItem("Isa do 3D/Cena/Copiar Componentes")]
        public static void Open()
        {
            GetWindow<ComponentCopyTool>("Copiar Componentes");
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Copiar Componentes", IsaDo3DStyles.Title);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Copia os componentes de um prefab para os objetos selecionados na cena.",
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Usar seleção", GUILayout.Width(100)))
                        _source = Selection.activeGameObject;
                    _source = (GameObject)EditorGUILayout.ObjectField(_source, typeof(GameObject), true);
                }

                _includeChildren = EditorGUILayout.Toggle("Incluir filhos", _includeChildren);
                _overwriteExisting = EditorGUILayout.Toggle(
                    new GUIContent("Sobrescrever componentes",
                        "Se o objeto novo já tem o componente, cola os valores por cima. Desligado, pula. " +
                        "Não vale pra layer, tag e Static."),
                    _overwriteExisting);
                _adaptColliders = EditorGUILayout.Toggle(
                    new GUIContent("Adaptar colliders",
                        "Escala o collider pela proporção entre o tamanho do objeto novo e o do prefab. " +
                        "MeshCollider passa a usar a mesh do objeto novo."),
                    _adaptColliders);
                _remapReferences = EditorGUILayout.Toggle(
                    new GUIContent("Remapear referências",
                        "Referências que apontam pra dentro do prefab (ele mesmo ou os filhos) passam a apontar pro objeto equivalente no objeto novo."),
                    _remapReferences);

                // Fechado esconde os toggles, então o título avisa se tem algum ligado
                int activeProperties = (_copyLayer ? 1 : 0) + (_copyTag ? 1 : 0) + (_copyStatic ? 1 : 0);
                var propertiesTitle = activeProperties > 0 ? $"Propriedades do objeto ({activeProperties} ligada(s))" : "Propriedades do objeto";
                _showObjectProperties = EditorGUILayout.Foldout(_showObjectProperties, propertiesTitle, true);
                if (_showObjectProperties)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        _copyLayer = EditorGUILayout.Toggle(
                            new GUIContent("Copiar layer", "Passa a layer do objeto do prefab pro objeto novo."),
                            _copyLayer);
                        _copyTag = EditorGUILayout.Toggle(
                            new GUIContent("Copiar tag", "Passa a tag do objeto do prefab pro objeto novo."),
                            _copyTag);
                        _copyStatic = EditorGUILayout.Toggle(
                            new GUIContent("Copiar flags Static", "Passa as flags de Static (GI, batching, occlusion, navegação) do prefab pro objeto novo."),
                            _copyStatic);
                    }
                }

                var targets = SceneTargets();
                using (new EditorGUI.DisabledScope(_source == null || targets.Length == 0))
                {
                    if (GUILayout.Button($"Colar ({targets.Length})"))
                        CopyToTargets(targets);
                }
            }
        }

        // Asset do Project não aceita componente novo por aqui
        private GameObject[] SceneTargets()
        {
            return Selection.gameObjects
                .Where(g => g != _source && g.scene.IsValid())
                .ToArray();
        }

        private void CopyToTargets(GameObject[] targets)
        {
            Undo.SetCurrentGroupName("Copiar Componentes");
            int undoGroup = Undo.GetCurrentGroup();

            var stats = new Stats();

            foreach (var target in targets)
            {
                var pasted = new List<Component>();
                CopyNode(_source, target, stats, pasted);

                if (_includeChildren) CopyChildren(target, stats, pasted);

                // Só depois de colar tudo, a referência pode ser pra um componente de um filho que ainda não existia
                if (_remapReferences) RemapReferences(pasted, _source.transform, target.transform, stats);

                EditorSceneManager.MarkSceneDirty(target.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);

            var summary = $"[Copiar Componentes] '{_source.name}' em {targets.Length} objeto(s): " +
                          $"{stats.Copied} adicionado(s), {stats.Updated} sobrescrito(s), {stats.Skipped} pulado(s).";
            if (_copyLayer) summary += $" {stats.Layers} layer(s) copiada(s) pra '{LayerMask.LayerToName(_source.layer)}'.";
            if (_copyTag) summary += $" {stats.Tags} tag(s) copiada(s).";
            if (_copyStatic) summary += $" {stats.Statics} flag(s) de Static copiada(s).";
            if (_remapReferences) summary += $" {stats.Remapped} referência(s) remapeada(s).";
            if (stats.Unmapped > 0) summary += $" {stats.Unmapped} referência(s) continuam apontando pro prefab (sem correspondente no destino).";
            if (_adaptColliders) summary += $" {stats.Adapted} collider(s) adaptado(s).";
            if (stats.Unfitted > 0) summary += $" {stats.Unfitted} collider(s) ficaram com o tamanho do prefab (sem mesh pra comparar).";
            if (stats.Failed > 0) summary += $" {stats.Failed} falharam (veja os avisos acima).";
            if (stats.MissingChild > 0) summary += $" {stats.MissingChild} filho(s) sem correspondente no destino.";
            Debug.Log(summary);
        }

        private void CopyChildren(GameObject target, Stats stats, List<Component> pasted)
        {
            foreach (var child in _source.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject == _source) continue;

                var match = target.transform.Find(RelativePath(child, _source.transform));
                if (match == null)
                {
                    stats.MissingChild++;
                    continue;
                }

                CopyNode(child.gameObject, match.gameObject, stats, pasted);
            }
        }

        private void CopyNode(GameObject from, GameObject to, Stats stats, List<Component> pastedAll)
        {
            CopyObjectProperties(from, to, stats);

            var seen = new Dictionary<System.Type, int>();

            foreach (var component in from.GetComponents<Component>())
            {
                // Script ausente vem null
                if (component == null || component is Transform) continue;

                var type = component.GetType();
                seen.TryGetValue(type, out int index);
                seen[type] = index + 1;

                // Pareia pelo índice dentro do mesmo tipo, tem objeto com dois AudioSource
                var existing = to.GetComponents(type).Where(c => c.GetType() == type).ToArray();

                Component pasted;
                if (index < existing.Length)
                {
                    if (!_overwriteExisting)
                    {
                        stats.Skipped++;
                        continue;
                    }

                    if (!ComponentUtility.CopyComponent(component) || !ComponentUtility.PasteComponentValues(existing[index]))
                    {
                        Fail(component, to, stats);
                        continue;
                    }

                    pasted = existing[index];
                    stats.Updated++;
                }
                else
                {
                    if (!ComponentUtility.CopyComponent(component) || !ComponentUtility.PasteComponentAsNew(to))
                    {
                        Fail(component, to, stats);
                        continue;
                    }

                    pasted = to.GetComponents(type).Last(c => c.GetType() == type);
                    stats.Copied++;
                }

                pastedAll.Add(pasted);

                if (_adaptColliders && component is Collider sourceCollider && pasted is Collider targetCollider)
                    AdaptCollider(sourceCollider, targetCollider, from.transform, to.transform, stats);
            }
        }

        // Layer, tag e Static ficam no GameObject, o ComponentUtility não leva
        private void CopyObjectProperties(GameObject from, GameObject to, Stats stats)
        {
            if (_copyLayer && to.layer != from.layer)
            {
                Undo.RecordObject(to, "Copiar layer");
                to.layer = from.layer;
                stats.Layers++;
            }

            if (_copyTag && !to.CompareTag(from.tag))
            {
                Undo.RecordObject(to, "Copiar tag");
                to.tag = from.tag;
                stats.Tags++;
            }

            if (_copyStatic)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(from);
                if (GameObjectUtility.GetStaticEditorFlags(to) != flags)
                {
                    Undo.RecordObject(to, "Copiar flags Static");
                    GameObjectUtility.SetStaticEditorFlags(to, flags);
                    stats.Statics++;
                }
            }
        }

        private static void RemapReferences(List<Component> components, Transform sourceRoot, Transform targetRoot, Stats stats)
        {
            foreach (var component in components)
            {
                if (component == null) continue;

                var serialized = new SerializedObject(component);
                bool changed = false;

                for (var property = serialized.GetIterator(); property.Next(true);)
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    // Dono do componente, não é referência do usuário
                    if (property.name == "m_GameObject" || property.name == "m_Script") continue;

                    var value = property.objectReferenceValue;
                    if (value == null || !BelongsTo(value, sourceRoot)) continue;

                    if (TryMapReference(value, sourceRoot, targetRoot, out var mapped))
                    {
                        property.objectReferenceValue = mapped;
                        changed = true;
                        stats.Remapped++;
                    }
                    else
                    {
                        stats.Unmapped++;
                    }
                }

                if (changed) serialized.ApplyModifiedProperties();
            }
        }

        private static bool BelongsTo(Object value, Transform root)
        {
            var owner = OwnerOf(value);
            return owner != null && owner.transform.IsChildOf(root);
        }

        private static GameObject OwnerOf(Object value)
        {
            if (value is GameObject go) return go;
            return value is Component component ? component.gameObject : null;
        }

        private static bool TryMapReference(Object value, Transform sourceRoot, Transform targetRoot, out Object mapped)
        {
            mapped = null;

            var sourceObject = OwnerOf(value);
            var targetTransform = sourceObject.transform == sourceRoot
                ? targetRoot
                : targetRoot.Find(RelativePath(sourceObject.transform, sourceRoot));
            if (targetTransform == null) return false;

            if (value is GameObject)
            {
                mapped = targetTransform.gameObject;
                return true;
            }

            // Pareia pelo índice dentro do mesmo tipo, igual ao da cópia
            var type = value.GetType();
            int index = sourceObject.GetComponents(type).Where(c => c.GetType() == type).ToList().IndexOf((Component)value);
            var candidates = targetTransform.GetComponents(type).Where(c => c.GetType() == type).ToArray();
            if (index < 0 || index >= candidates.Length) return false;

            mapped = candidates[index];
            return true;
        }

        private static void Fail(Component component, GameObject to, Stats stats)
        {
            stats.Failed++;
            Debug.LogWarning($"[Copiar Componentes] Não deu pra copiar {component.GetType().Name} pra '{to.name}'.", to);
        }

        // Mantém a relação collider/mesh do prefab, senão um caixote maior ficaria com o collider do menor
        private static void AdaptCollider(Collider source, Collider target, Transform sourceNode, Transform targetNode, Stats stats)
        {
            if (source is MeshCollider)
            {
                var mesh = FindMesh(targetNode);
                if (mesh == null) { stats.Unfitted++; return; }

                Undo.RecordObject(target, "Adaptar collider");
                ((MeshCollider)target).sharedMesh = mesh;
                stats.Adapted++;
                return;
            }

            if (!TryGetLocalBounds(sourceNode, out var from) || !TryGetLocalBounds(targetNode, out var to))
            {
                stats.Unfitted++;
                return;
            }

            var ratio = new Vector3(Ratio(to.size.x, from.size.x), Ratio(to.size.y, from.size.y), Ratio(to.size.z, from.size.z));
            Vector3 MapCenter(Vector3 center) => to.center + Vector3.Scale(center - from.center, ratio);

            Undo.RecordObject(target, "Adaptar collider");

            switch (source)
            {
                case BoxCollider sourceBox:
                    var box = (BoxCollider)target;
                    box.center = MapCenter(sourceBox.center);
                    box.size = Vector3.Scale(sourceBox.size, ratio);
                    break;

                case SphereCollider sourceSphere:
                    var sphere = (SphereCollider)target;
                    sphere.center = MapCenter(sourceSphere.center);
                    sphere.radius = sourceSphere.radius * Mathf.Max(ratio.x, ratio.y, ratio.z);
                    break;

                case CapsuleCollider sourceCapsule:
                    var capsule = (CapsuleCollider)target;
                    int axis = sourceCapsule.direction;
                    float across = Mathf.Max(ratio[(axis + 1) % 3], ratio[(axis + 2) % 3]);
                    capsule.center = MapCenter(sourceCapsule.center);
                    capsule.height = sourceCapsule.height * ratio[axis];
                    capsule.radius = sourceCapsule.radius * across;
                    break;

                default:
                    return;
            }

            stats.Adapted++;
        }

        // Eixo achatado ou sem tamanho não dá pra dividir
        private static float Ratio(float target, float source)
        {
            return source < 1e-5f || target < 1e-5f ? 1f : target / source;
        }

        private static Mesh FindMesh(Transform node)
        {
            var filter = node.GetComponentInChildren<MeshFilter>(true);
            if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;

            var skinned = node.GetComponentInChildren<SkinnedMeshRenderer>(true);
            return skinned != null ? skinned.sharedMesh : null;
        }

        // No espaço do próprio nó, assim a escala do objeto não entra na conta
        private static bool TryGetLocalBounds(Transform node, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var toNode = node.worldToLocalMatrix;

            foreach (var renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    local = skinned.localBounds;
                }
                else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    local = filter.sharedMesh.bounds;
                }
                else
                {
                    continue;
                }

                var matrix = toNode * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var point = matrix.MultiplyPoint3x4(corner);

                    if (found) bounds.Encapsulate(point);
                    else bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
            }

            return found;
        }

        private static string RelativePath(Transform child, Transform root)
        {
            var path = child.name;
            for (var t = child.parent; t != null && t != root; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }
    }
}
