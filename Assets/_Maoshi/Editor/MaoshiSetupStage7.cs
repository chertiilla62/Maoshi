using Maoshi.Showdown;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 7: «Кошачья разборка». Добавляет ShowdownManager в префаб SessionManager
    /// (дуэль живёт рядом с сессией и переживает смену сцен).
    /// Запуск: Maoshi → Setup → Stage 7 - Cat Showdown.
    /// </summary>
    public static class MaoshiSetupStage7
    {
        [MenuItem("Maoshi/Setup/Stage 7 - Cat Showdown")]
        public static void SetupStage7()
        {
            string path = MaoshiSetupStage3.SessionManagerPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogError("[Maoshi Setup] Нет префаба SessionManager. Сначала запусти Stage 3.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<ShowdownManager>() == null)
                    root.AddComponent<ShowdownManager>();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 7 готов: «Кошачья разборка» подключена к SessionManager.");
        }
    }
}
