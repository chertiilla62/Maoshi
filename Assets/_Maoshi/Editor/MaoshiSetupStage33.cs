using Maoshi.Session;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 33: роль зрителя для выбывших. Добавляет SpectatorSystem в префаб SessionManager
    /// (он живёт всю сессию и работает в любой мини-игре на выбывание).
    /// Запуск: Maoshi → Setup → Stage 33 - Spectators.
    /// </summary>
    public static class MaoshiSetupStage33
    {
        [MenuItem("Maoshi/Setup/Stage 33 - Spectators")]
        public static void Setup()
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
                if (root.GetComponent<SpectatorSystem>() == null) root.AddComponent<SpectatorSystem>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 33 готов: зрители (Tab — следить за другим, R — тапок).");
        }
    }
}
