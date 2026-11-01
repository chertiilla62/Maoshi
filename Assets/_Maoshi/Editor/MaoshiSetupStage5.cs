using System.Collections.Generic;
using Maoshi.Gameplay;
using Maoshi.MiniGames.LastInBox;
using Maoshi.MiniGames.MouseHoles;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 5: мини-игры «Мышиные норки» и «Последний в коробке».
    /// Запуск: Maoshi → Setup → Stage 5 - Mouse Holes And Box. Обе сцены пересоздаются.
    /// </summary>
    public static class MaoshiSetupStage5
    {
        private const string MouseFolder = MaoshiSetupStage3.MiniGamesFolder + "/MouseHoles";
        private const string BoxFolder = MaoshiSetupStage3.MiniGamesFolder + "/LastInBox";
        private const string MouseScenePath = MaoshiSetup.ScenesFolder + "/MouseHoles.unity";
        private const string BoxScenePath = MaoshiSetup.ScenesFolder + "/LastInBox.unity";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        [MenuItem("Maoshi/Setup/Stage 5 - Mouse Holes And Box")]
        public static void SetupStage5()
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

            MaoshiSetup.EnsureFolder(MouseFolder);
            MaoshiSetup.EnsureFolder(BoxFolder);

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);

            var mouse = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(MouseFolder + "/MouseHoles.asset");
            mouse.EditorSetup("mouse_holes", "Мышиные норки",
                "Мыши выскакивают из норок. Лови больше всех! Золотая мышь — 3 очка.",
                "MouseHoles", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(mouse);

            var box = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(BoxFolder + "/LastInBox.asset");
            box.EditorSetup("last_in_box", "Последний в коробке",
                "Все в коробку! Кто снаружи при проверке — выбыл. Коробка всё меньше.",
                "LastInBox", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(box);

            catalog.EditorAdd(mouse);
            catalog.EditorAdd(box);
            EditorUtility.SetDirty(catalog);

            CreateMouseScene(catPrefab);
            CreateBoxScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(MouseScenePath);
            MaoshiSetupStage3.AddSceneToBuild(BoxScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 5 готов: «Мышиные норки» и «Последний в коробке».");
        }

        // ───────────────────────── Мышиные норки ─────────────────────────

        private static void CreateMouseScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupCamera(new Color(0.62f, 0.80f, 0.70f));

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material fence = MaoshiSetupStage3.LoadMaterial("Fence");
            Material dark = MaoshiSetupStage3.LoadMaterial("CatEyes");
            Material nose = MaoshiSetupStage3.LoadMaterial("CatNose");
            Material mouseMaterial = GetOrCreateMaterial("Mouse", new Color(0.6f, 0.6f, 0.62f));

            // Площадка с низким бортиком.
            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(18f, 1f, 18f), ground);
            Borders(9f, 0.6f, fence);

            // Норки сеткой 4 × 3.
            var holes = new List<MouseHole>();
            float[] xs = { -4.5f, -1.5f, 1.5f, 4.5f };
            float[] zs = { -3f, 0f, 3f };
            var holesRoot = new GameObject("Holes");
            foreach (float z in zs)
            {
                foreach (float x in xs)
                    holes.Add(CreateHole(holesRoot.transform, new Vector3(x, 0f, z), dark, nose, mouseMaterial, holes.Count));
            }

            Spawner(catPrefab, Vector3.zero, CatSpawner.SpawnLayout.Circle, 6.5f);

            var modeGo = new GameObject("MouseHolesMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<MouseHolesMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "mouse_holes";
            serialized.FindProperty("timeLimitSeconds").floatValue = 60f;
            var holesProperty = serialized.FindProperty("holes");
            holesProperty.arraySize = holes.Count;
            for (int i = 0; i < holes.Count; i++)
                holesProperty.GetArrayElementAtIndex(i).objectReferenceValue = holes[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, MouseScenePath);
        }

        private static MouseHole CreateHole(Transform parent, Vector3 position, Material dark, Material nose, Material mouseMaterial, int index)
        {
            var root = new GameObject($"MouseHole{index:00}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            // Тёмное пятно норки на земле.
            var pit = Visual(PrimitiveType.Cylinder, "Pit", root.transform, new Vector3(0f, 0.011f, 0f), new Vector3(0.8f, 0.01f, 0.8f), Vector3.zero, dark);

            // Мышка из кубиков. Прячется под землёй, выглядывает по сигналу.
            var mouse = new GameObject("Mouse");
            mouse.transform.SetParent(root.transform, false);
            mouse.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            var body = Visual(PrimitiveType.Cube, "Body", mouse.transform, new Vector3(0f, 0.09f, 0f), new Vector3(0.22f, 0.18f, 0.38f), Vector3.zero, mouseMaterial);
            var head = Visual(PrimitiveType.Cube, "Head", mouse.transform, new Vector3(0f, 0.12f, 0.24f), new Vector3(0.16f, 0.14f, 0.14f), Vector3.zero, mouseMaterial);
            var earL = Visual(PrimitiveType.Cube, "EarL", mouse.transform, new Vector3(-0.07f, 0.22f, 0.22f), new Vector3(0.07f, 0.07f, 0.02f), Vector3.zero, mouseMaterial);
            var earR = Visual(PrimitiveType.Cube, "EarR", mouse.transform, new Vector3(0.07f, 0.22f, 0.22f), new Vector3(0.07f, 0.07f, 0.02f), Vector3.zero, mouseMaterial);
            Visual(PrimitiveType.Cube, "Nose", mouse.transform, new Vector3(0f, 0.12f, 0.32f), new Vector3(0.04f, 0.04f, 0.03f), Vector3.zero, nose);
            Visual(PrimitiveType.Cube, "Tail", mouse.transform, new Vector3(0f, 0.06f, -0.34f), new Vector3(0.03f, 0.03f, 0.32f), new Vector3(-15f, 0f, 0f), nose);
            mouse.SetActive(false);

            var hole = root.AddComponent<MouseHole>();
            var serialized = new SerializedObject(hole);
            serialized.FindProperty("mouse").objectReferenceValue = mouse.transform;
            var renderers = serialized.FindProperty("mouseRenderers");
            var tinted = new[] { body, head, earL, earR };
            renderers.arraySize = tinted.Length;
            for (int i = 0; i < tinted.Length; i++)
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = tinted[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            _ = pit;
            return hole;
        }

        // ───────────────────────── Последний в коробке ─────────────────────────

        private static void CreateBoxScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupCamera(new Color(0.70f, 0.78f, 0.95f));

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material brick = MaoshiSetupStage3.LoadMaterial("Brick");
            Material cardboard = GetOrCreateMaterial("Cardboard", new Color(0.80f, 0.62f, 0.40f));
            Material cardboardDark = GetOrCreateMaterial("CardboardDark", new Color(0.62f, 0.46f, 0.28f));

            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(22f, 1f, 22f), ground);

            // Островок выбывших: между ним и площадкой провал шире прыжка кота.
            MaoshiSetupStage3.Block("BenchIsland", new Vector3(18.5f, -0.5f, 0f), new Vector3(6f, 1f, 10f), brick);
            var bench = new GameObject("Bench");
            bench.transform.SetPositionAndRotation(new Vector3(18.5f, 0f, 0f), Quaternion.LookRotation(Vector3.left));

            // Коробка: четыре стенки с коллайдерами и дно без коллайдера.
            var boxRoot = new GameObject("Box");
            var walls = new Transform[4];
            string[] names = { "WallNorth", "WallSouth", "WallEast", "WallWest" };
            for (int i = 0; i < 4; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = names[i];
                wall.transform.SetParent(boxRoot.transform, false);
                wall.GetComponent<Renderer>().sharedMaterial = cardboard;
                walls[i] = wall.transform;
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(boxRoot.transform, false);
            floor.GetComponent<Renderer>().sharedMaterial = cardboardDark;

            var boxVisual = boxRoot.AddComponent<BoxVisual>();
            var boxSerialized = new SerializedObject(boxVisual);
            boxSerialized.FindProperty("wallNorth").objectReferenceValue = walls[0];
            boxSerialized.FindProperty("wallSouth").objectReferenceValue = walls[1];
            boxSerialized.FindProperty("wallEast").objectReferenceValue = walls[2];
            boxSerialized.FindProperty("wallWest").objectReferenceValue = walls[3];
            boxSerialized.FindProperty("floor").objectReferenceValue = floor.transform;
            boxSerialized.ApplyModifiedPropertiesWithoutUndo();
            boxVisual.SetSize(4f); // чтобы в редакторе коробка выглядела собранной

            Spawner(catPrefab, Vector3.zero, CatSpawner.SpawnLayout.Circle, 6.5f);

            var modeGo = new GameObject("LastInBoxMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<LastInBoxMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "last_in_box";
            serialized.FindProperty("timeLimitSeconds").floatValue = 100f;
            serialized.FindProperty("box").objectReferenceValue = boxVisual;
            serialized.FindProperty("bench").objectReferenceValue = bench.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, BoxScenePath);
        }

        // ───────────────────────── Общее ─────────────────────────

        private static void SetupCamera(Color background)
        {
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null) return;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, -12f), Quaternion.Euler(40f, 0f, 0f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
        }

        private static void Borders(float halfSize, float height, Material material)
        {
            var root = new GameObject("Borders");
            float length = halfSize * 2f + 0.4f;
            MaoshiSetupStage3.Block("BorderN", new Vector3(0f, height * 0.5f, halfSize), new Vector3(length, height, 0.4f), material).transform.SetParent(root.transform, true);
            MaoshiSetupStage3.Block("BorderS", new Vector3(0f, height * 0.5f, -halfSize), new Vector3(length, height, 0.4f), material).transform.SetParent(root.transform, true);
            MaoshiSetupStage3.Block("BorderE", new Vector3(halfSize, height * 0.5f, 0f), new Vector3(0.4f, height, length), material).transform.SetParent(root.transform, true);
            MaoshiSetupStage3.Block("BorderW", new Vector3(-halfSize, height * 0.5f, 0f), new Vector3(0.4f, height, length), material).transform.SetParent(root.transform, true);
        }

        private static void Spawner(GameObject catPrefab, Vector3 position, CatSpawner.SpawnLayout layout, float radius)
        {
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = position;
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            serialized.FindProperty("layout").enumValueIndex = (int)layout;
            serialized.FindProperty("spawnRadius").floatValue = radius;
            serialized.FindProperty("spawnHeight").floatValue = 0.3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Renderer Visual(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

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

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", 0.15f);
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
