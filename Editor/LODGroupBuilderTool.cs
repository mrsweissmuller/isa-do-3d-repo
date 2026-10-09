#if ISADO3D_LOD_BUILDER
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class LODGroupBuilderTool
    {
        // [0-9] porque o \d aceita dígito árabe e o int.Parse quebra
        private static readonly Regex LodSuffix = new Regex(@"_LOD([0-9]{1,3})$", RegexOptions.IgnoreCase);

        private const float BaseTransitionHeight = 0.5f;
        private const float TransitionFalloff = 0.5f;

        [MenuItem("GameObject/LOD Group Builder (Isa do 3D)", true)]
        private static bool ValidateBuild()
        {
            foreach (var go in Selection.gameObjects)
                if (TryResolveLodRoot(go, out _, out _))
                    return true;
            return false;
        }

        [MenuItem("GameObject/LOD Group Builder (Isa do 3D)", false, 0)]
        [MenuItem("Isa do 3D/Cena/LOD Group Builder")]
        public static void BuildFromSelection()
        {
            var selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                EditorUtility.DisplayDialog("LOD Group Builder",
                    "Selecione o objeto raiz (o Empty {base}) que tem os filhos _LOD0/_LOD1/...", "OK");
                return;
            }

            var resolved = new List<(GameObject Root, SortedDictionary<int, List<Renderer>> Lods)>();
            foreach (var go in selection)
            {
                if (!TryResolveLodRoot(go, out var root, out var lods)) continue;
                if (resolved.Any(r => r.Root == root)) continue;

                resolved.Add((root, lods));
            }

            if (resolved.Count == 0)
            {
                EditorUtility.DisplayDialog("LOD Group Builder",
                    "Nenhuma hierarquia de LOD encontrada na seleção.\n\n" +
                    "Esperado: um pai com filhos nomeados {base}_LOD0, {base}_LOD1, {base}_LOD2...\n" +
                    "(é o que o add-on Auto LOD do Blender exporta).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Montar LODGroup");
            int undoGroup = Undo.GetCurrentGroup();

            int built = 0;
            foreach (var (root, byIndex) in resolved)
            {
                if (byIndex.Count < 2)
                    Debug.LogWarning($"[LOD Group Builder] '{root.name}' só tem um nível de LOD, então o LODGroup vai controlar só o cull.");

                int levels = ApplyLodGroup(root, byIndex);
                built++;
                Debug.Log($"[LOD Group Builder] LODGroup montado em '{root.name}' com {levels} nível(is).");
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[LOD Group Builder] Concluído: {built} LODGroup(s) montado(s).");
        }

        private static bool TryResolveLodRoot(GameObject go, out GameObject root, out SortedDictionary<int, List<Renderer>> lods)
        {
            root = null;
            lods = null;
            if (go == null) return false;

            if (CollectLodChildren(go, out var found))
            {
                root = go;
                lods = found;
                return true;
            }

            if (LodSuffix.IsMatch(go.name) && go.transform.parent != null)
            {
                var parent = go.transform.parent.gameObject;
                if (CollectLodChildren(parent, out var fromParent))
                {
                    root = parent;
                    lods = fromParent;
                    return true;
                }
            }

            return false;
        }

        private static bool CollectLodChildren(GameObject root, out SortedDictionary<int, List<Renderer>> lods)
        {
            lods = new SortedDictionary<int, List<Renderer>>();
            foreach (Transform child in root.transform)
            {
                var m = LodSuffix.Match(child.name);
                if (!m.Success) continue;

                int index = int.Parse(m.Groups[1].Value);
                var renderers = child.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                if (!lods.TryGetValue(index, out var list))
                {
                    list = new List<Renderer>();
                    lods[index] = list;
                }
                list.AddRange(renderers);
            }
            return lods.Count > 0;
        }

        private static int ApplyLodGroup(GameObject root, SortedDictionary<int, List<Renderer>> byIndex)
        {
            var ordered = byIndex.Values.ToList();
            int count = ordered.Count;
            var lods = new LOD[count];
            for (int i = 0; i < count; i++)
            {
                float height = BaseTransitionHeight * Mathf.Pow(TransitionFalloff, i);
                lods[i] = new LOD(height, ordered[i].ToArray());
            }

            var group = root.GetComponent<LODGroup>();
            if (group == null)
                group = Undo.AddComponent<LODGroup>(root);
            else
                Undo.RecordObject(group, "Atualizar LODGroup");

            group.SetLODs(lods);
            group.RecalculateBounds();
            EditorUtility.SetDirty(group);

            return count;
        }
    }
}
#endif //ISADO3D_LOD_BUILDER
