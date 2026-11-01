using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maoshi.Network;
using Maoshi.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Автоматическая сборка сцен и настроек проекта. Запуск: меню Maoshi → Setup.
    /// Повторный запуск безопасен: Bootstrap пересобирается, а MainMenu и Hub создаются, только если их нет.
    /// </summary>
    public static class MaoshiSetup
    {
        public const string Root = "Assets/_Maoshi";
        public const string ScenesFolder = Root + "/Scenes";
        public const string PrefabsFolder = Root + "/Prefabs";
        public const string NetworkFolder = Root + "/Network";

        public const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        public const string MainMenuScenePath = ScenesFolder + "/MainMenu.unity";
        public const string HubScenePath = ScenesFolder + "/Hub.unity";
        public const string NetworkPlayerPrefabPath = PrefabsFolder + "/NetworkPlayer.prefab";
        public const string PrefabsListPath = NetworkFolder + "/MaoshiNetworkPrefabs.asset";

        [MenuItem("Maoshi/Setup/Stage 1 - Network Scenes")]
        public static void SetupStage1()
        {
            // Не трогаем чужие несохранённые правки: просим сохранить сцену вручную.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(NetworkFolder);

            // Тестовые копии игры работают в фоне, иначе неактивное окно «замирает» и рвёт соединение.
            PlayerSettings.runInBackground = true;

            GameObject playerPrefab = CreateNetworkPlayerPrefab();
            NetworkPrefabsList prefabsList = GetOrCreatePrefabsList(playerPrefab);

            CreateMainMenuSceneIfMissing();
            CreateHubSceneIfMissing();
            CreateBootstrapScene(playerPrefab, prefabsList);

            UpdateBuildSettings();
            MaoshiPlayModeBootstrap.Apply();

            // Оставляем открытым главное меню: в нём удобнее работать, Play всё равно стартует с Bootstrap.
            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 1 готов: сцены Bootstrap, MainMenu и Hub созданы и добавлены в Build Settings.");
        }

        // ───────────────────────── Префаб игрока ─────────────────────────

        private static GameObject CreateNetworkPlayerPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPlayerPrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("NetworkPlayer");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkPlayer>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, NetworkPlayerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Находит список сетевых префабов (свой или созданный NGO автоматически) и кладёт туда игрока.</summary>
        public static NetworkPrefabsList GetOrCreatePrefabsList(GameObject playerPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabsListPath);

            if (list == null)
            {
                string guid = AssetDatabase.FindAssets("t:NetworkPrefabsList").FirstOrDefault();
                if (guid != null)
                    list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(AssetDatabase.GUIDToAssetPath(guid));
            }

            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabsListPath);
            }

            if (!list.PrefabList.Any(p => p.Prefab == playerPrefab))
            {
                list.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(list);
            }

            return list;
        }

        // ───────────────────────── Сцены ─────────────────────────

        private static void CreateMainMenuSceneIfMissing()
        {
            if (File.Exists(MainMenuScenePath)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.13f, 0.20f); // сумеречный двор
            }

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        private static void CreateHubSceneIfMissing()
        {
            if (File.Exists(HubScenePath)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Заглушка двора: просто земля. Настоящий хаб — этап 4.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -16f), Quaternion.Euler(35f, 0f, 0f));
            }

            EditorSceneManager.SaveScene(scene, HubScenePath);
        }

        /// <summary>Служебная стартовая сцена: NetworkManager + ConnectionManager + UI. Пересобирается каждый раз.</summary>
        private static void CreateBootstrapScene(GameObject playerPrefab, NetworkPrefabsList prefabsList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Камера-заглушка: без неё окна (в том числе виртуальные игроки Multiplayer Play Mode)
            // показывают «No cameras rendering», пока Bootstrap не перешёл в главное меню.
            var cameraGo = new GameObject("BootstrapCamera");
            var bootstrapCamera = cameraGo.AddComponent<Camera>();
            bootstrapCamera.clearFlags = CameraClearFlags.SolidColor;
            bootstrapCamera.backgroundColor = new Color(0.16f, 0.13f, 0.20f);

            var go = new GameObject("NetworkManager");

            var transport = go.AddComponent<UnityTransport>();
            // Быстрый отказ, если хост не найден: 10 попыток по 1 секунде вместо минуты ожидания.
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 10;

            var networkManager = go.AddComponent<NetworkManager>();
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.TickRate = 60; // плавнее движение чужих котов (по умолчанию 30)

            var lists = networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (!lists.Contains(prefabsList)) lists.Add(prefabsList);

            go.AddComponent<ConnectionManager>();
            // HUD и префаб SessionManager (если этап 3 уже собран).
            MaoshiSetupStage3.ConfigureBootstrapObject(go);

            EditorUtility.SetDirty(networkManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        // ───────────────────────── Build Settings ─────────────────────────

        /// <summary>Bootstrap первым, затем меню и хаб. Остальные сцены (кроме SampleScene) сохраняются после них.</summary>
        private static void UpdateBuildSettings()
        {
            var ours = new[] { BootstrapScenePath, MainMenuScenePath, HubScenePath };
            var scenes = new List<EditorBuildSettingsScene>();

            foreach (var path in ours)
                scenes.Add(new EditorBuildSettingsScene(path, true));

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (ours.Contains(scene.path)) continue;
                if (scene.path.EndsWith("/SampleScene.unity")) continue;
                scenes.Add(scene);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
