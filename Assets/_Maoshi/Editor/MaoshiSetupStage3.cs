using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maoshi.Gameplay;
using Maoshi.MiniGames.LastOnFence;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 3: каталог мини-игр, SessionManager, HUD и сцена «Последний на заборе».
    /// Запуск: Maoshi → Setup → Stage 3 - Session And Fence. Сцена LastOnFence пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage3
    {
        public const string MiniGamesFolder = MaoshiSetup.Root + "/MiniGames";
        public const string CatalogPath = MiniGamesFolder + "/MiniGameCatalog.asset";
        public const string SessionManagerPrefabPath = MaoshiSetup.PrefabsFolder + "/SessionManager.prefab";

        private const string FenceFolder = MiniGamesFolder + "/LastOnFence";
        private const string FenceDefinitionPath = FenceFolder + "/LastOnFence.asset";
        private const string FenceScenePath = MaoshiSetup.ScenesFolder + "/LastOnFence.unity";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        [MenuItem("Maoshi/Setup/Stage 3 - Session And Fence")]
        public static void SetupStage3()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            var catPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoshiSetupStage2.CatPrefabPath);
            if (catPrefab == null)
            {
                Debug.LogError("[Maoshi Setup] Нет префаба кота. Сначала запусти Stage 2.");
                return;
            }

            MaoshiSetup.EnsureFolder(FenceFolder);

            var catalog = GetOrCreate<MiniGameCatalog>(CatalogPath);
            var fence = GetOrCreate<MiniGameDefinition>(FenceDefinitionPath);
            fence.EditorSetup(
                "last_on_fence",
                "Последний на заборе",
                "Толкайтесь на узком заборе. Кто упал — выбыл. Забор рушится с краёв!",
                "LastOnFence",
                new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(fence);
            catalog.EditorAdd(fence);
            EditorUtility.SetDirty(catalog);

            var sessionPrefab = CreateSessionManagerPrefab(catalog);
            MaoshiSetup.GetOrCreatePrefabsList(sessionPrefab);

            CreateFenceScene(catPrefab);
            AddSceneToBuild(FenceScenePath);
            ConfigureBootstrap();

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 3 готов: SessionManager, HUD и мини-игра «Последний на заборе».");
        }

        /// <summary>Подключает к Bootstrap игровой HUD и префаб SessionManager. Вызывается и из Stage 1.</summary>
        public static void ConfigureBootstrapObject(GameObject networkManagerObject)
        {
            // Интерфейс (UI Toolkit), EventSystem и счётчик FPS — этап 11.
            MaoshiSetupStage11.ConfigureUI(networkManagerObject);

            var connection = networkManagerObject.GetComponent<ConnectionManager>();
            var sessionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionManagerPrefabPath);
            if (connection != null && sessionPrefab != null)
            {
                var serialized = new SerializedObject(connection);
                serialized.FindProperty("sessionManagerPrefab").objectReferenceValue = sessionPrefab.GetComponent<NetworkObject>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ConfigureBootstrap()
        {
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.BootstrapScenePath, OpenSceneMode.Single);
            var connection = Object.FindFirstObjectByType<ConnectionManager>();
            if (connection == null)
            {
                Debug.LogError("[Maoshi Setup] В Bootstrap нет ConnectionManager. Запусти Stage 1.");
                return;
            }

            ConfigureBootstrapObject(connection.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static GameObject CreateSessionManagerPrefab(MiniGameCatalog catalog)
        {
            var go = new GameObject("SessionManager");
            go.AddComponent<NetworkObject>();
            var session = go.AddComponent<SessionManager>();
            go.AddComponent<Maoshi.Showdown.ShowdownManager>(); // «Кошачья разборка» (этап 7)
            go.AddComponent<SpectatorSystem>(); // роль зрителя для выбывших (этап 33)

            var serialized = new SerializedObject(session);
            serialized.FindProperty("catalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, SessionManagerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ───────────────────────── Сцена забора ─────────────────────────

        private static void CreateFenceScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(-9f, 7f, 0f), Quaternion.Euler(25f, 90f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.98f, 0.75f, 0.55f); // закатное небо над двором
            }

            Material ground = LoadMaterial("Ground");
            Material fenceMaterial = LoadMaterial("Fence");
            Material wood = LoadMaterial("Wood");
            Material brick = LoadMaterial("Brick");

            Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), ground);

            // Забор из секций по 1 м: верх на высоте 2.5, ширина 0.5 — тесно, но стоять можно.
            const int segmentCount = 14;
            const float fenceTop = 2.5f;
            var fenceRoot = new GameObject("Fence");
            var segments = new List<Transform>();
            for (int i = 0; i < segmentCount; i++)
            {
                float z = (i - (segmentCount - 1) * 0.5f) * 1f;
                var segment = Block($"Segment{i:00}", new Vector3(0f, fenceTop * 0.5f, z), new Vector3(0.5f, fenceTop, 0.98f),
                    i % 2 == 0 ? fenceMaterial : wood);
                segment.transform.SetParent(fenceRoot.transform, true);
                segments.Add(segment.transform);
            }

            // Скамейка выбывших сбоку: оттуда видно забор, а запрыгнуть обратно нельзя (забор выше прыжка).
            Block("BenchPlatform", new Vector3(6.25f, 0.1f, 0f), new Vector3(4f, 0.2f, 7f), brick);
            var bench = new GameObject("Bench");
            bench.transform.SetPositionAndRotation(new Vector3(7.5f, 0.2f, 0f), Quaternion.LookRotation(Vector3.left));

            // Спавн котов в линию по забору.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = new Vector3(0f, fenceTop, 0f);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Line;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 1.4f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Правила мини-игры.
            var modeGo = new GameObject("LastOnFenceMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<LastOnFenceMode>();
            var modeSerialized = new SerializedObject(mode);
            modeSerialized.FindProperty("miniGameId").stringValue = "last_on_fence";
            modeSerialized.FindProperty("countdownSeconds").floatValue = 3f;
            modeSerialized.FindProperty("timeLimitSeconds").floatValue = 90f;
            modeSerialized.FindProperty("fenceTopY").floatValue = fenceTop;
            modeSerialized.FindProperty("bench").objectReferenceValue = bench.transform;
            var segmentsProperty = modeSerialized.FindProperty("segments");
            segmentsProperty.arraySize = segments.Count;
            for (int i = 0; i < segments.Count; i++)
                segmentsProperty.GetArrayElementAtIndex(i).objectReferenceValue = segments[i];
            modeSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, FenceScenePath);

            // Повторное открытие и сохранение: NGO генерирует ID сетевых объектов сцены при загрузке.
            var reopened = EditorSceneManager.OpenScene(FenceScenePath, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        public static GameObject Block(string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        public static Material LoadMaterial(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/M_{name}.mat");

        public static void AddSceneToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
