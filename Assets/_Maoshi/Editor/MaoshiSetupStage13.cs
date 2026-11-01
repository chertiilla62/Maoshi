using System.Collections.Generic;
using Maoshi.Gameplay;
using Maoshi.MiniGames.YarnRam;
using Maoshi.Session;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 13: мини-игра «Клубок-таран».
    /// Плоская крыша 14×14 м на высоте 8 м, труба и вентиляция — препятствия, от которых отскакивают клубки.
    /// 6 клубков: 3 на старте, остальные ждут внутри дома и падают с неба по ходу игры.
    /// Запуск: Maoshi → Setup → Stage 13 - Yarn Ram. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage13
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/YarnRam";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/YarnRam.unity";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        private const float BuildingHeight = 8f;
        private const float RoofTop = 8.1f;
        private const float RoofHalf = 7f;
        private const float BallRadius = 0.8f;
        private const int StartBalls = 3;

        [MenuItem("Maoshi/Setup/Stage 13 - Yarn Ram")]
        public static void SetupStage13()
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

            MaoshiSetup.EnsureFolder(Folder);

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/YarnRam.asset");
            definition.EditorSetup("yarn_ram", "Клубок-таран",
                "Пинай гигантские клубки (ЛКМ / F) и сноси соперников с крыши! Упал — выбыл.",
                "YarnRam", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 13 готов: «Клубок-таран».");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 17f, -15f), Quaternion.Euler(38f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.96f, 0.70f, 0.52f); // закат над крышами
            }

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material brick = MaoshiSetupStage3.LoadMaterial("Brick");
            Material roof = GetOrCreateMaterial("RoofTar", new Color(0.36f, 0.34f, 0.38f));
            Material metal = GetOrCreateMaterial("RoofMetal", new Color(0.62f, 0.64f, 0.68f));
            Material chimney = GetOrCreateMaterial("Chimney", new Color(0.55f, 0.30f, 0.24f));

            // Двор и дом.
            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, -4f), new Vector3(40f, 1f, 40f), ground);
            MaoshiSetupStage3.Block("Building", new Vector3(0f, BuildingHeight * 0.5f, 0f), new Vector3(RoofHalf * 2f, BuildingHeight, RoofHalf * 2f), brick);
            MaoshiSetupStage3.Block("Roof", new Vector3(0f, BuildingHeight + 0.05f, 0f), new Vector3(RoofHalf * 2f, 0.1f, RoofHalf * 2f), roof);

            // Препятствия на крыше: от них отскакивают клубки, за ними можно спрятаться.
            MaoshiSetupStage3.Block("Chimney", new Vector3(-4f, RoofTop + 1f, 3.5f), new Vector3(1.2f, 2f, 1.2f), chimney);
            MaoshiSetupStage3.Block("Vent", new Vector3(4.2f, RoofTop + 0.4f, -3.2f), new Vector3(1.6f, 0.8f, 1.1f), metal);
            MaoshiSetupStage3.Block("Skylight", new Vector3(-2.5f, RoofTop + 0.2f, -4.2f), new Vector3(2f, 0.4f, 1.4f), metal);

            // Клубки.
            Color[] colors =
            {
                new(0.86f, 0.30f, 0.30f), new(0.30f, 0.52f, 0.86f), new(0.95f, 0.78f, 0.25f),
                new(0.36f, 0.72f, 0.42f), new(0.62f, 0.40f, 0.82f), new(0.95f, 0.55f, 0.25f),
            };
            var storage = new GameObject("BallStorage");
            storage.transform.position = new Vector3(-5f, 3f, 0f); // внутри дома — не видно
            var balls = new List<YarnBall>();
            for (int i = 0; i < colors.Length; i++)
            {
                Vector3 position;
                if (i < StartBalls)
                {
                    float angle = i * Mathf.PI * 2f / StartBalls + 0.5f;
                    position = new Vector3(Mathf.Cos(angle) * 2f, RoofTop + BallRadius, Mathf.Sin(angle) * 2f);
                }
                else position = storage.transform.position + Vector3.right * (i * 2f);

                balls.Add(CreateBall($"YarnBall{i}", position, colors[i], i >= StartBalls));
            }

            // Скамейка выбывших — во дворе перед домом.
            var bench = new GameObject("Bench");
            bench.transform.SetPositionAndRotation(new Vector3(0f, 0.2f, -11f), Quaternion.LookRotation(Vector3.forward));

            // Коты по кругу на крыше.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = new Vector3(0f, RoofTop, 0f);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Circle;
            spawnerSerialized.FindProperty("spawnRadius").floatValue = 4.6f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Правила.
            var modeGo = new GameObject("YarnRamMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<YarnRamMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "yarn_ram";
            serialized.FindProperty("timeLimitSeconds").floatValue = 90f;
            serialized.FindProperty("roofY").floatValue = RoofTop;
            serialized.FindProperty("roofHalfSize").floatValue = RoofHalf;
            serialized.FindProperty("startBalls").intValue = StartBalls;
            serialized.FindProperty("ballStorage").objectReferenceValue = storage.transform;
            serialized.FindProperty("bench").objectReferenceValue = bench.transform;
            var ballsProperty = serialized.FindProperty("balls");
            ballsProperty.arraySize = balls.Count;
            for (int i = 0; i < balls.Count; i++)
                ballsProperty.GetArrayElementAtIndex(i).objectReferenceValue = balls[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>Клубок: шар с «нитками» (тонкие кольца без коллайдеров), физика на сервере.</summary>
        private static YarnBall CreateBall(string name, Vector3 position, Color color, bool parked)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (BallRadius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Yarn_" + ColorUtility.ToHtmlStringRGB(color), color);

            // Нитки: три тонких кольца разного наклона, чуть темнее клубка.
            Material threads = GetOrCreateMaterial("YarnThread_" + ColorUtility.ToHtmlStringRGB(color), color * 0.75f);
            Vector3[] tilts = { new(0f, 0f, 0f), new(60f, 0f, 30f), new(-50f, 40f, 0f) };
            for (int i = 0; i < tilts.Length; i++)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Thread" + i;
                Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.transform.SetParent(go.transform, false);
                ring.transform.localEulerAngles = tilts[i];
                ring.transform.localScale = new Vector3(1.02f, 0.03f, 1.02f);
                ring.GetComponent<Renderer>().sharedMaterial = threads;
            }

            var body = go.AddComponent<Rigidbody>();
            body.mass = 2.5f;
            body.linearDamping = 0.15f;
            body.angularDamping = 0.4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.isKinematic = parked;

            go.AddComponent<NetworkObject>();
            var netTransform = go.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            var netBody = go.AddComponent<NetworkRigidbody>();
            netBody.UseRigidBodyForMotion = false; // плавно у клиентов

            return go.AddComponent<YarnBall>();
        }

        // ───────────────────────── Общее ─────────────────────────

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialsFolder}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) lit = Shader.Find("Standard");
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }

            color.a = 1f;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Сохранить сцену и пересохранить после открытия — так NGO выдаёт ID сетевым объектам.</summary>
        private static void SaveWithNetworkIds(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);

            var reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }
    }
}
