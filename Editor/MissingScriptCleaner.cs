#if ISADO3D_MISSING_SCRIPTS
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IsaDo3D.EnvironmentArt
{
    public class MissingScriptCleaner : EditorWindow
    {
        private readonly List<GameObject> _found = new List<GameObject>();
        private Vector2 _scroll;
        private bool _hasScanned;

        [MenuItem("Isa do 3D/QA/Missing Scripts")]
        public static void Open()
        {
            GetWindow<MissingScriptCleaner>("Missing Scripts");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Acha objetos com componentes de script quebrados (\"Missing Script\") na cena aberta. " +
                "Usa GameObjectUtility do próprio Unity pra remover, não mexe em mais nada do objeto.",
                MessageType.None);

            if (GUILayout.Button("Buscar na Cena Aberta"))
            {
                Scan();
                GUIUtility.ExitGUI();
            }

            if (!_hasScanned)
            {
                EditorGUILayout.HelpBox("Clique em Buscar para verificar.", MessageType.Info);
                return;
            }

            _found.RemoveAll(go => go == null);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{_found.Count} objeto(s) com script ausente.", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_found.Count == 0))
            {
                if (GUILayout.Button($"Remover de Todos ({_found.Count})"))
                {
                    RemoveAll();
                    GUIUtility.ExitGUI();
                }
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                foreach (var go in _found)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(GetPath(go))) SelectAndFrame(go);
                        if (GUILayout.Button("Remover", GUILayout.Width(80)))
                        {
                            RemoveFrom(go);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void Scan()
        {
            _found.Clear();
            _hasScanned = true;

            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                ScanRecursive(root.transform);

            Debug.Log($"[Missing Scripts] {_found.Count} objeto(s) com script ausente encontrado(s) em '{scene.name}'.");
        }

        private void ScanRecursive(Transform node)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) > 0)
                _found.Add(node.gameObject);

            for (int i = 0; i < node.childCount; ++i)
                ScanRecursive(node.GetChild(i));
        }

        private void RemoveAll()
        {
            Undo.SetCurrentGroupName("Remover Scripts Ausentes");
            int undoGroup = Undo.GetCurrentGroup();

            int totalRemoved = 0;
            foreach (var go in _found.ToArray())
            {
                if (go == null) continue;
                totalRemoved += RemoveFrom(go);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Missing Scripts] {totalRemoved} componente(s) de script ausente removido(s) no total.");
        }

        private int RemoveFrom(GameObject go)
        {
            // RemoveMonoBehavioursWithMissingScript não tem Undo sozinho
            Undo.RegisterCompleteObjectUndo(go, "Remover Scripts Ausentes");
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            _found.Remove(go);
            if (removed > 0) EditorSceneManager.MarkSceneDirty(go.scene);
            return removed;
        }

        private static string GetPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + " > " + path;
                parent = parent.parent;
            }
            return path;
        }

        private static void SelectAndFrame(GameObject go)
        {
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }
    }
}
#endif //ISADO3D_MISSING_SCRIPTS
