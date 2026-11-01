using System.Collections.Generic;
using Maoshi.Gameplay;
using Maoshi.MiniGames.JanitorEscape;
using Maoshi.Session;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 14: мини-игра «Побег от дворника».
    /// Двор 34×26 м за забором: машина, мусорные баки, лавочки, песочница, клумба, деревья, дворницкая.
    /// NavMesh запекается при сборке сцены. 3 дворника ждут у дворницкой и выходят по очереди.
    /// Запуск: Maoshi → Setup → Stage 14 - Janitor Escape. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage14
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/JanitorEscape";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/JanitorEscape.unity";
        private const string NavMeshPath = Folder + "/JanitorEscape_NavMesh.asset";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        private const float HalfX = 17f;
        private const float HalfZ = 13f;
        private const int JanitorCount = 3;

        [MenuItem("Maoshi/Setup/Stage 14 - Janitor Escape")]
        public static void SetupStage14()
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/JanitorEscape.asset");
            definition.EditorSetup("janitor_escape", "Побег от дворника",
                "Дворник с метлой гоняет котов по двору. Замёл — выбыл. Последний непойманный получает больше всех рыбы!",
                "JanitorEscape", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 14 готов: «Побег от дворника».");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 22f, -24f), Quaternion.Euler(45f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.62f, 0.80f, 0.95f); // утро, дворник вышел на смену
            }

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material brick = MaoshiSetupStage3.LoadMaterial("Brick");
            Material fence = MaoshiSetupStage3.LoadMaterial("Fence");
            Material asphalt = GetOrCreateMaterial("Asphalt", new Color(0.40f, 0.40f, 0.43f));
            Material carPaint = GetOrCreateMaterial("CarRed", new Color(0.78f, 0.22f, 0.20f));
            Material glass = GetOrCreateMaterial("CarGlass", new Color(0.55f, 0.72f, 0.82f));
            Material bin = GetOrCreateMaterial("BinGreen", new Color(0.24f, 0.46f, 0.30f));
            Material wood = GetOrCreateMaterial("BenchWood", new Color(0.62f, 0.42f, 0.24f));
            Material sand = GetOrCreateMaterial("Sand", new Color(0.92f, 0.82f, 0.56f));
            Material flowers = GetOrCreateMaterial("Flowerbed", new Color(0.42f, 0.62f, 0.30f));
            Material trunk = GetOrCreateMaterial("TreeTrunk", new Color(0.42f, 0.30f, 0.20f));
            Material leaves = GetOrCreateMaterial("TreeLeaves", new Color(0.36f, 0.62f, 0.32f));
            Material shed = GetOrCreateMaterial("ShedBlue", new Color(0.36f, 0.48f, 0.66f));

            // Земля (с запасом под скамейку выбывших за забором) и асфальт двора.
            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, -6f), new Vector3(44f, 1f, 44f), ground);
            MaoshiSetupStage3.Block("Asphalt", new Vector3(0f, 0.005f, 0f), new Vector3(HalfX * 2f, 0.01f, HalfZ * 2f), asphalt);

            // Стена дома сзади, заборы по бокам и спереди (выше прыжка кота).
            MaoshiSetupStage3.Block("HouseWall", new Vector3(0f, 4f, HalfZ + 0.5f), new Vector3(HalfX * 2f + 1f, 8f, 1f), brick);
            MaoshiSetupStage3.Block("FenceFront", new Vector3(0f, 1.3f, -HalfZ - 0.15f), new Vector3(HalfX * 2f + 1f, 2.6f, 0.3f), fence);
            MaoshiSetupStage3.Block("FenceLeft", new Vector3(-HalfX - 0.15f, 1.3f, 0f), new Vector3(0.3f, 2.6f, HalfZ * 2f), fence);
            MaoshiSetupStage3.Block("FenceRight", new Vector3(HalfX + 0.15f, 1.3f, 0f), new Vector3(0.3f, 2.6f, HalfZ * 2f), fence);

            // Дворницкая в углу: отсюда выходят дворники.
            MaoshiSetupStage3.Block("Shed", new Vector3(-13.5f, 1.5f, 9f), new Vector3(6f, 3f, 4f), shed);
            MaoshiSetupStage3.Block("ShedRoof", new Vector3(-13.5f, 3.1f, 9f), new Vector3(6.4f, 0.2f, 4.4f), brick);
            NoCollider(MaoshiSetupStage3.Block("ShedDoor", new Vector3(-10.48f, 1f, 9f), new Vector3(0.05f, 2f, 1.2f), wood));

            // Машина — высокая, не запрыгнуть, но можно обежать.
            MaoshiSetupStage3.Block("Car", new Vector3(8f, 0.7f, -5f), new Vector3(4.2f, 1.4f, 1.9f), carPaint);
            MaoshiSetupStage3.Block("CarCabin", new Vector3(7.7f, 1.75f, -5f), new Vector3(2.2f, 0.7f, 1.7f), glass);

            // Мусорные баки у стены дома.
            MaoshiSetupStage3.Block("Bin1", new Vector3(11f, 0.65f, 10.8f), new Vector3(2f, 1.3f, 1.3f), bin);
            MaoshiSetupStage3.Block("Bin2", new Vector3(13.6f, 0.65f, 10.8f), new Vector3(2f, 1.3f, 1.3f), bin);
            MaoshiSetupStage3.Block("Bin3", new Vector3(-2f, 0.65f, -10.5f), new Vector3(2f, 1.3f, 1.3f), bin);

            // Лавочки — низкие, на них можно запрыгнуть (но метла достанет).
            MaoshiSetupStage3.Block("Bench1", new Vector3(-6f, 0.25f, -6f), new Vector3(2.6f, 0.5f, 0.7f), wood);
            MaoshiSetupStage3.Block("Bench2", new Vector3(5f, 0.25f, 6.5f), new Vector3(2.6f, 0.5f, 0.7f), wood);
            MaoshiSetupStage3.Block("Bench3", new Vector3(14f, 0.25f, -2f), new Vector3(0.7f, 0.5f, 2.6f), wood);

            // Песочница с бортиками.
            Vector3 box = new Vector3(-4f, 0f, 4.5f);
            MaoshiSetupStage3.Block("Sandbox", box + new Vector3(0f, 0.02f, 0f), new Vector3(4f, 0.04f, 4f), sand);
            MaoshiSetupStage3.Block("SandboxN", box + new Vector3(0f, 0.15f, 2f), new Vector3(4.2f, 0.3f, 0.2f), wood);
            MaoshiSetupStage3.Block("SandboxS", box + new Vector3(0f, 0.15f, -2f), new Vector3(4.2f, 0.3f, 0.2f), wood);
            MaoshiSetupStage3.Block("SandboxW", box + new Vector3(-2f, 0.15f, 0f), new Vector3(0.2f, 0.3f, 4.2f), wood);
            MaoshiSetupStage3.Block("SandboxE", box + new Vector3(2f, 0.15f, 0f), new Vector3(0.2f, 0.3f, 4.2f), wood);

            // Клумба в центре.
            MaoshiSetupStage3.Block("Flowerbed", new Vector3(2f, 0.4f, -1f), new Vector3(3f, 0.8f, 3f), flowers);

            // Деревья: коллайдер только у ствола — вокруг них удобно петлять.
            Tree(new Vector3(-11f, 0f, -4f), trunk, leaves);
            Tree(new Vector3(-1f, 0f, -6.5f), trunk, leaves);
            Tree(new Vector3(10f, 0f, 3f), trunk, leaves);
            Tree(new Vector3(-12f, 0f, 2.5f), trunk, leaves);

            // NavMesh — до дворников и котов, чтобы они не вырезали в нём дыр.
            BakeNavMesh();

            // Дворники ждут у двери дворницкой.
            var janitors = new List<Janitor>();
            for (int i = 0; i < JanitorCount; i++)
            {
                var position = new Vector3(-9.3f, 0f, 9f + (i - 1) * 1.4f);
                janitors.Add(CreateJanitor($"Janitor{i}", position, i));
            }

            // Скамейка выбывших — за передним забором, смотрит во двор.
            var bench = new GameObject("Bench");
            bench.transform.SetPositionAndRotation(new Vector3(0f, 0.2f, -16.5f), Quaternion.LookRotation(Vector3.forward));

            // Коты по кругу в середине двора.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = new Vector3(2f, 0f, -1f);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Circle;
            spawnerSerialized.FindProperty("spawnRadius").floatValue = 4.5f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Правила.
            var modeGo = new GameObject("JanitorEscapeMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<JanitorEscapeMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "janitor_escape";
            serialized.FindProperty("timeLimitSeconds").floatValue = 90f;
            serialized.FindProperty("bench").objectReferenceValue = bench.transform;
            var janitorsProperty = serialized.FindProperty("janitors");
            janitorsProperty.arraySize = janitors.Count;
            for (int i = 0; i < janitors.Count; i++)
                janitorsProperty.GetArrayElementAtIndex(i).objectReferenceValue = janitors[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, ScenePath);
        }

        private static void BakeNavMesh()
        {
            var navGo = new GameObject("NavMesh");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                Debug.LogError("[Maoshi Setup] NavMesh не запёкся.");
                return;
            }

            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            EditorUtility.SetDirty(surface);
        }

        private static void Tree(Vector3 position, Material trunk, Material leaves)
        {
            var trunkGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunkGo.name = "TreeTrunk";
            trunkGo.transform.position = position + new Vector3(0f, 1.5f, 0f);
            trunkGo.transform.localScale = new Vector3(0.5f, 1.5f, 0.5f);
            trunkGo.GetComponent<Renderer>().sharedMaterial = trunk;

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "TreeCrown";
            Object.DestroyImmediate(crown.GetComponent<Collider>());
            crown.transform.SetParent(trunkGo.transform, true);
            crown.transform.position = position + new Vector3(0f, 3.8f, 0f);
            crown.transform.localScale = new Vector3(6f, 2.2f, 6f); // в локальных единицах ствола
            crown.GetComponent<Renderer>().sharedMaterial = leaves;
        }

        /// <summary>Дворник из примитивов: сапоги, штаны, оранжевый жилет, голова, кепка и метла.</summary>
        private static Janitor CreateJanitor(string name, Vector3 position, int index)
        {
            Material skin = GetOrCreateMaterial("JanitorSkin", new Color(0.93f, 0.74f, 0.60f));
            Material vest = GetOrCreateMaterial("JanitorVest", new Color(0.98f, 0.52f, 0.12f));
            Material pants = GetOrCreateMaterial("JanitorPants", new Color(0.22f, 0.26f, 0.36f));
            Material cap = GetOrCreateMaterial("JanitorCap", new Color(0.20f, 0.22f, 0.24f));
            Material handle = GetOrCreateMaterial("BroomHandle", new Color(0.55f, 0.38f, 0.22f));
            Material twigs = GetOrCreateMaterial("BroomTwigs", new Color(0.80f, 0.66f, 0.36f));

            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.right));

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            Part(PrimitiveType.Cube, "LegL", body, new Vector3(-0.15f, 0.45f, 0f), new Vector3(0.22f, 0.9f, 0.26f), pants);
            Part(PrimitiveType.Cube, "LegR", body, new Vector3(0.15f, 0.45f, 0f), new Vector3(0.22f, 0.9f, 0.26f), pants);
            Part(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 1.25f, 0f), new Vector3(0.62f, 0.45f, 0.42f), vest);
            Part(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.82f, 0f), new Vector3(0.36f, 0.38f, 0.36f), skin);
            Part(PrimitiveType.Sphere, "Nose", body, new Vector3(0f, 1.8f, 0.19f), new Vector3(0.09f, 0.08f, 0.09f), skin);
            Part(PrimitiveType.Cylinder, "Cap", body, new Vector3(0f, 1.99f, 0f), new Vector3(0.38f, 0.06f, 0.38f), cap);
            Part(PrimitiveType.Cube, "CapVisor", body, new Vector3(0f, 1.95f, 0.2f), new Vector3(0.3f, 0.03f, 0.18f), cap);
            Part(PrimitiveType.Capsule, "ArmR", body, new Vector3(0.36f, 1.25f, 0.12f), new Vector3(0.14f, 0.3f, 0.14f), vest);

            // Метла: шарнир у плеча, черенок вдоль локальной оси Z, на конце — пучок прутьев.
            var pivot = new GameObject("BroomPivot").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.38f, 1.3f, 0.15f);
            var stick = Part(PrimitiveType.Cylinder, "BroomStick", pivot, new Vector3(0f, 0f, 0.8f), new Vector3(0.05f, 0.8f, 0.05f), handle);
            stick.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Cube, "BroomTwigs", pivot, new Vector3(0f, 0f, 1.75f), new Vector3(0.38f, 0.22f, 0.55f), twigs);

            // Физика и сеть.
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.95f, 0f);
            capsule.height = 1.9f;
            capsule.radius = 0.38f;
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 1.9f;
            agent.angularSpeed = 720f;
            agent.acceleration = 30f;
            agent.stoppingDistance = 0.6f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 40 + index;

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;

            var janitor = root.AddComponent<Janitor>();
            var serialized = new SerializedObject(janitor);
            serialized.FindProperty("broomPivot").objectReferenceValue = pivot;
            serialized.FindProperty("bodyRoot").objectReferenceValue = body;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return janitor;
        }

        private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static void NoCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
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
