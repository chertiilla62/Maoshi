using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 41: шерсть котов убрана совсем (решение пользователя).
    /// - В префабе Cat вложенная модель AnimX (Cat_R_Red) распаковывается, объекты Cat.R_Fur_LOD0–4 удаляются,
    ///   LODGroup пересобирается без них.
    /// - У окрасок (CatAppearance.breeds) ссылки на материалы меха обнуляются — текстуры меха 4K больше не попадают в сборку.
    /// Повторный запуск безопасен. Если пересобрать кота этапами 2/8/9 — запустить этот этап снова.
    /// </summary>
    public static class MaoshiSetupStage41
    {
        private const string CatPrefab = "Assets/_Maoshi/Prefabs/Cat.prefab";

        [MenuItem("Maoshi/Setup/Stage 41 - Remove Cat Fur")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(CatPrefab);
            try
            {
                // Вложенный префаб модели — распаковываем, иначе его детей удалить нельзя.
                var nested = root.GetComponentsInChildren<Transform>(true)
                    .Select(t => t.gameObject)
                    .Where(g => PrefabUtility.IsOutermostPrefabInstanceRoot(g))
                    .ToList();
                foreach (var instance in nested)
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                var fur = root.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("_Fur_")).ToList();
                var furSet = new HashSet<Renderer>(fur);

                foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = group.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        lods[i].renderers = lods[i].renderers.Where(r => r != null && !furSet.Contains(r)).ToArray();
                    group.SetLODs(lods);
                }

                foreach (var renderer in fur) Object.DestroyImmediate(renderer.gameObject);

                int cleared = 0;
                foreach (var appearance in root.GetComponentsInChildren<Maoshi.Cat.CatAppearance>(true))
                {
                    var so = new SerializedObject(appearance);
                    var breeds = so.FindProperty("breeds");
                    if (breeds == null) continue;
                    for (int i = 0; i < breeds.arraySize; i++)
                    {
                        var furProperty = breeds.GetArrayElementAtIndex(i).FindPropertyRelative("fur");
                        if (furProperty != null && furProperty.objectReferenceValue != null)
                        {
                            furProperty.objectReferenceValue = null;
                            cleared++;
                        }
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, CatPrefab);
                Debug.Log($"[Maoshi Stage 41] Шерсть убрана: удалено объектов меха {fur.Count}, распаковано вложенных моделей {nested.Count}, " +
                          $"обнулено материалов меха у окрасок {cleared}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
