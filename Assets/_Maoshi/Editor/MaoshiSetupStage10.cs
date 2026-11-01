using Maoshi.Gameplay;
using Maoshi.MiniGames.FlyHunt;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 10: мини-игра «Охота на муху».
    /// Площадка с бортиком и ящиками разной высоты (с них удобно прыгать и на них присаживается муха).
    /// Запуск: Maoshi → Setup → Stage 10 - Fly Hunt. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage10
    {
        private const string FlyFolder = MaoshiSetupStage3.MiniGamesFolder + "/FlyHunt";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/FlyHunt.unity";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        [MenuItem("Maoshi/Setup/Stage 10 - Fly Hunt")]
        public static void SetupStage10()
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

            MaoshiSetup.EnsureFolder(FlyFolder);

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(FlyFolder + "/FlyHunt.asset");
            definition.EditorSetup("fly_hunt", "Охота на муху",
                "Над двором летает муха. Допрыгни и поймай! Толкай соперников в прыжке.",
                "FlyHunt", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 10 готов: «Охота на муху».");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, -12f), Quaternion.Euler(40f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.70f, 0.84f, 0.92f);
            }

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material fence = MaoshiSetupStage3.LoadMaterial("Fence");
            Material wood = MaoshiSetupStage3.LoadMaterial("Wood");
            Material black = MaoshiSetupStage3.LoadMaterial("CatEyes");
            Material wingMaterial = GetOrCreateMaterial("FlyWing", new Color(0.85f, 0.92f, 0.98f));
            Material eyeMaterial = GetOrCreateMaterial("FlyEye", new Color(0.75f, 0.12f, 0.10f));
            Material shadowMaterial = GetOrCreateMaterial("FlyShadow", new Color(0.18f, 0.18f, 0.2f));

            // Площадка 16 × 16 с бортиком.
            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(18f, 1f, 18f), ground);
            Borders(8.5f, 0.8f, fence);

            // Ящики разной высоты: с них можно допрыгнуть выше, на них иногда садится муха.
            var cratesRoot = new GameObject("Crates");
            var perchRoot = new GameObject("Perches");
            (Vector3 position, float size, float height)[] crates =
            {
                (new Vector3(-4f, 0f, 3.5f), 1.4f, 0.5f),
                (new Vector3(4.5f, 0f, 4f), 1.2f, 0.9f),
                (new Vector3(-4.5f, 0f, -3.5f), 1.2f, 0.9f),
                (new Vector3(4f, 0f, -4f), 1.4f, 0.5f),
                (new Vector3(0f, 0f, 0f), 1.6f, 1.2f),
            };
            var perches = new Transform[crates.Length];
            for (int i = 0; i < crates.Length; i++)
            {
                var (position, size, height) = crates[i];
                var crate = MaoshiSetupStage3.Block($"Crate{i}", position + Vector3.up * (height * 0.5f), new Vector3(size, height, size), wood);
                crate.isStatic = true;
                crate.transform.SetParent(cratesRoot.transform, true);

                var perch = new GameObject($"Perch{i}");
                perch.transform.SetParent(perchRoot.transform, false);
                perch.transform.position = position + Vector3.up * height;
                perches[i] = perch.transform;
            }

            var fly = CreateFly(new Vector3(0f, 1.6f, 2f), black, wingMaterial, eyeMaterial, shadowMaterial);

            Spawner(catPrefab, new Vector3(0f, 0f, 0f), CatSpawner.SpawnLayout.Circle, 6f);

            var modeGo = new GameObject("FlyHuntMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<FlyHuntMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "fly_hunt";
            serialized.FindProperty("timeLimitSeconds").floatValue = 60f;
            serialized.FindProperty("fly").objectReferenceValue = fly;
            var perchesProperty = serialized.FindProperty("perches");
            perchesProperty.arraySize = perches.Length;
            for (int i = 0; i < perches.Length; i++)
                perchesProperty.GetArrayElementAtIndex(i).objectReferenceValue = perches[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>Муха из примитивов: тельце, глаза, два крыла на шарнирах и тень на земле. Без коллайдеров.</summary>
        private static FlyVisual CreateFly(Vector3 position, Material black, Material wing, Material eye, Material shadowMaterial)
        {
            var root = new GameObject("Fly");
            root.transform.position = position;

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            Visual(PrimitiveType.Sphere, "Thorax", body.transform, new Vector3(0f, 0f, 0.02f), new Vector3(0.18f, 0.16f, 0.2f), Vector3.zero, black);
            Visual(PrimitiveType.Sphere, "Abdomen", body.transform, new Vector3(0f, -0.01f, -0.14f), new Vector3(0.16f, 0.14f, 0.22f), Vector3.zero, black);
            Visual(PrimitiveType.Sphere, "EyeL", body.transform, new Vector3(-0.06f, 0.03f, 0.12f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, eye);
            Visual(PrimitiveType.Sphere, "EyeR", body.transform, new Vector3(0.06f, 0.03f, 0.12f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, eye);

            var wingLeft = new GameObject("WingLeft").transform;
            wingLeft.SetParent(body.transform, false);
            wingLeft.localPosition = new Vector3(-0.06f, 0.07f, -0.02f);
            Visual(PrimitiveType.Cube, "Blade", wingLeft, new Vector3(-0.13f, 0f, -0.03f), new Vector3(0.26f, 0.01f, 0.12f), Vector3.zero, wing);

            var wingRight = new GameObject("WingRight").transform;
            wingRight.SetParent(body.transform, false);
            wingRight.localPosition = new Vector3(0.06f, 0.07f, -0.02f);
            Visual(PrimitiveType.Cube, "Blade", wingRight, new Vector3(0.13f, 0f, -0.03f), new Vector3(0.26f, 0.01f, 0.12f), Vector3.zero, wing);

            var shadow = Visual(PrimitiveType.Cylinder, "Shadow", root.transform, new Vector3(0f, -position.y + 0.02f, 0f), new Vector3(0.35f, 0.005f, 0.35f), Vector3.zero, shadowMaterial);

            var visual = root.AddComponent<FlyVisual>();
            var serialized = new SerializedObject(visual);
            serialized.FindProperty("body").objectReferenceValue = body.transform;
            serialized.FindProperty("wingLeft").objectReferenceValue = wingLeft;
            serialized.FindProperty("wingRight").objectReferenceValue = wingRight;
            serialized.FindProperty("shadow").objectReferenceValue = shadow.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return visual;
        }

        // ───────────────────────── Общее ─────────────────────────

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
            material.SetFloat("_Smoothness", 0.3f);
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
