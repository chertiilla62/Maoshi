using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.PondFishing;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 27: мини-игра «Рыбалка лапой в пруду».
    /// Вечерний сквер, пруд 13×13 м (углы прикрыты камнями и камышом), 8 кочек-пней по кругу (по одной на кота),
    /// дно пруда ниже воды — свалился, значит в воде. Кочки по очереди уходят под воду (PondStone).
    /// Запуск: Maoshi → Setup → Stage 27 - Pond Fishing. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage27
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/PondFishing";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/PondFishing.unity";

        private const float Half = 6.5f;
        private const float WaterY = -0.15f;
        private const float BottomY = -1.6f;
        private const float StoneRing = 3.4f;
        private const float StoneTop = 0.35f;

        [MenuItem("Maoshi/Setup/Stage 27 - Pond Fishing")]
        public static void Setup()
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/PondFishing.asset");
            definition.EditorSetup("pond_fishing", "Рыбалка лапой",
                "Коты на кочках посреди пруда бьют лапой по теням рыб. Крупная дороже, но вырывается, золотая — самая быстрая. Кочки уходят под воду, а соседи толкаются!",
                "PondFishing", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 27 готов: «Рыбалка лапой» (в каталоге №{index + 1}).");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 9f, -12f), Quaternion.Euler(38f, 0f, 0f));

            var root = ArtKit.Group("Park");
            var grass = ArtLibrary.Grass;
            var dirt = ArtLibrary.Dirt;

            // Земля вокруг квадратной ямы пруда.
            float outer = 14f;
            float w = outer - Half;
            ArtKit.UvBox("GroundN", new Vector3(0f, -0.5f, Half + w * 0.5f), new Vector3(outer * 2f, 0.5f, w), grass, grass, new Vector2(4f, 4f), 4f, root);
            ArtKit.UvBox("GroundS", new Vector3(0f, -0.5f, -Half - w * 0.5f), new Vector3(outer * 2f, 0.5f, w), grass, grass, new Vector2(4f, 4f), 4f, root);
            ArtKit.UvBox("GroundW", new Vector3(-Half - w * 0.5f, -0.5f, 0f), new Vector3(w, 0.5f, Half * 2f), grass, grass, new Vector2(4f, 4f), 4f, root);
            ArtKit.UvBox("GroundE", new Vector3(Half + w * 0.5f, -0.5f, 0f), new Vector3(w, 0.5f, Half * 2f), grass, grass, new Vector2(4f, 4f), 4f, root);
            // Берега ямы (земля), дно, вода.
            ArtKit.UvBox("BankN", new Vector3(0f, BottomY, Half - 0.1f), new Vector3(Half * 2f, -BottomY, 0.2f), dirt, grass, new Vector2(2f, 2f), 2f, root);
            ArtKit.UvBox("BankS", new Vector3(0f, BottomY, -Half + 0.1f), new Vector3(Half * 2f, -BottomY, 0.2f), dirt, grass, new Vector2(2f, 2f), 2f, root);
            ArtKit.UvBox("BankW", new Vector3(-Half + 0.1f, BottomY, 0f), new Vector3(0.2f, -BottomY, Half * 2f), dirt, grass, new Vector2(2f, 2f), 2f, root);
            ArtKit.UvBox("BankE", new Vector3(Half - 0.1f, BottomY, 0f), new Vector3(0.2f, -BottomY, Half * 2f), dirt, grass, new Vector2(2f, 2f), 2f, root);
            ArtKit.Box("PondBottom", new Vector3(0f, BottomY - 0.25f, 0f), new Vector3(Half * 2f, 0.5f, Half * 2f), ArtKit.Color("PondMud", new Color(0.22f, 0.20f, 0.14f)), root);
            var water = ArtKit.Color("PondWater", new Color(0.20f, 0.52f, 0.58f), 0.75f);
            water.SetColor("_EmissionColor", new Color(0.02f, 0.06f, 0.07f));
            water.EnableKeyword("_EMISSION");
            var waterGo = ArtKit.Box("Water", new Vector3(0f, WaterY - 0.05f, 0f), new Vector3(Half * 2f - 0.2f, 0.1f, Half * 2f - 0.2f), water, root, false);
            waterGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Кувшинки (картинка) и камыш, камни по углам — пруд кажется круглым.
            var lily = ArtKit.Color("LilyPad", new Color(0.30f, 0.62f, 0.30f));
            var rng = new System.Random(3);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                float r = 1.5f + (float)rng.NextDouble() * 4.3f;
                if (Mathf.Abs(r - StoneRing) < 0.8f) r += 1.2f;
                var pad = ArtKit.Cylinder("LilyPad", new Vector3(Mathf.Cos(a) * r, WaterY + 0.005f, Mathf.Sin(a) * r), 0.25f + (float)rng.NextDouble() * 0.2f, 0.02f, lily, root, false);
                pad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var (cx, cz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                Vector3 corner = new Vector3(cx * (Half - 0.9f), BottomY, cz * (Half - 0.9f));
                ArtKit.PlaceFit("Nature/rock_smallA", corner, rng.Next(360), new Vector3(2.4f, 2.0f, 2.4f), root, true);
                ArtKit.PlaceFit("Nature/rock_smallC", corner + new Vector3(-cx * 1.2f, 0f, 0f), rng.Next(360), new Vector3(1.6f, 1.8f, 1.6f), root, true);
                ArtKit.PlaceFit("Nature/rock_smallC", corner + new Vector3(0f, 0f, -cz * 1.2f), rng.Next(360), new Vector3(1.6f, 1.8f, 1.6f), root, true);
                for (int k = 0; k < 4; k++)
                    ArtKit.PlaceFit("Nature/grass_large", new Vector3(cx * (Half + 0.3f) - cx * k * 0.5f, 0f, cz * (Half + 0.3f) - cz * (3 - k) * 0.4f), rng.Next(360), new Vector3(0.9f, 0.9f, 0.9f), root);
            }

            // Кочки-пни по кругу.
            var points = BuildStones(root);

            // Сквер: деревья, лавочки, фонари, дома вокруг.
            var surround = ArtLibrary.Surround(new Vector2(-outer, -outer), new Vector2(outer, outer), 6f, 91);
            // Общий «дальний асфальт» из Surround закрыл бы яму пруда — заменяем его рамкой вокруг сквера.
            var far = surround.Find("FarGround");
            if (far != null)
            {
                var farMaterial = far.GetComponent<Renderer>().sharedMaterial;
                Object.DestroyImmediate(far.gameObject);
                float big = 130f;
                foreach (var (c, size) in new[]
                {
                    (new Vector3(0f, -1.03f, outer + big * 0.5f), new Vector3(big * 2f + outer * 2f, 1f, big)),
                    (new Vector3(0f, -1.03f, -outer - big * 0.5f), new Vector3(big * 2f + outer * 2f, 1f, big)),
                    (new Vector3(outer + big * 0.5f, -1.03f, 0f), new Vector3(big, 1f, outer * 2f)),
                    (new Vector3(-outer - big * 0.5f, -1.03f, 0f), new Vector3(big, 1f, outer * 2f)),
                })
                    ArtKit.UvBox("FarGround", c, size, farMaterial, farMaterial, new Vector2(6f, 6f), 6f, surround, false);
            }
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(0f, 0f, -Half - 1.6f), 0f, new Vector3(1.9f, 1.3f, 0.9f), root, true);
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(Half + 1.6f, 0f, 0f), -90f, new Vector3(1.9f, 1.3f, 0.9f), root, true);
            ArtLibrary.Lamp(root, new Vector3(-Half - 1.4f, 0f, -Half - 1.4f));
            ArtLibrary.Lamp(root, new Vector3(Half + 1.4f, 0f, Half + 1.4f));
            foreach (var (x, z, model) in new[] { (-10f, 4f, "Nature/tree_oak"), (10f, -5f, "Nature/tree_default"), (-9f, -9f, "Nature/tree_fat"), (8f, 10f, "Nature/tree_detailed"), (-4f, 11f, "Nature/tree_tall") })
                ArtLibrary.Tree(root, model, new Vector3(x, 0f, z), 6.5f);
            InvisibleRing(root, outer);

            // Шаблоны и материалы для рыб.
            var shadow = ArtKit.Color("FishShadow", new Color(0.08f, 0.16f, 0.22f));
            var gold = ArtKit.Color("FishGold", new Color(1f, 0.72f, 0.15f), 0.6f);
            gold.SetColor("_EmissionColor", new Color(0.6f, 0.38f, 0.04f));
            gold.EnableKeyword("_EMISSION");
            var ring = ArtKit.Color("PawRing", new Color(0.85f, 0.92f, 0.95f));
            var ringActive = ArtKit.Color("PawRingActive", new Color(1f, 0.85f, 0.3f));
            ringActive.SetColor("_EmissionColor", new Color(0.9f, 0.6f, 0.1f));
            ringActive.EnableKeyword("_EMISSION");

            // Коты — по кочкам (в разнесённом порядке, чтобы двое не стояли рядом).
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = Vector3.zero;
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Points;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            int[] order = { 0, 4, 2, 6, 1, 5, 3, 7 };
            var pointsProperty = spawnerSerialized.FindProperty("points");
            pointsProperty.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++) pointsProperty.GetArrayElementAtIndex(i).objectReferenceValue = points[order[i]];
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("FishingMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<FishingMode>();

            var fishTemplate = ArtKit.PlaceFit("Food/fish", new Vector3(0f, -10f, 0f), 0f, new Vector3(0.5f, 0.3f, 0.5f), modeGo.transform);
            if (fishTemplate != null)
            {
                foreach (var t in fishTemplate.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                foreach (var c in fishTemplate.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }

            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "pond_fishing";
            serialized.FindProperty("timeLimitSeconds").floatValue = 75f;
            serialized.FindProperty("pondCenter").vector3Value = Vector3.zero;
            serialized.FindProperty("waterY").floatValue = WaterY;
            serialized.FindProperty("reach").floatValue = 1.05f;
            serialized.FindProperty("wetSeconds").floatValue = 1f;
            serialized.FindProperty("shadowMaterial").objectReferenceValue = shadow;
            serialized.FindProperty("goldMaterial").objectReferenceValue = gold;
            serialized.FindProperty("ringMaterial").objectReferenceValue = ring;
            serialized.FindProperty("ringActiveMaterial").objectReferenceValue = ringActive;
            serialized.FindProperty("fishModel").objectReferenceValue = fishTemplate;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 35f, 140f);
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>8 кочек-пней на кольце; возвращает точки на их верхушках. Фазы погружения — по очереди.</summary>
        private static List<Transform> BuildStones(Transform root)
        {
            var bark = ArtKit.Color("StumpBark", new Color(0.42f, 0.30f, 0.20f));
            var moss = ArtKit.Color("StumpMoss", new Color(0.42f, 0.58f, 0.26f));
            var points = new List<Transform>();
            float bottom = -0.9f;
            float height = StoneTop - bottom;
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * StoneRing, 0f, Mathf.Sin(a) * StoneRing);
                float radius = 0.58f;

                var stone = new GameObject($"Stone{i}");
                stone.transform.SetParent(root, false);
                stone.transform.position = new Vector3(p.x, bottom + height * 0.5f, p.z);
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Stump";
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(stone.transform, false);
                visual.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                visual.GetComponent<Renderer>().sharedMaterial = bark;
                var top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                top.name = "Moss";
                Object.DestroyImmediate(top.GetComponent<Collider>());
                top.transform.SetParent(stone.transform, false);
                top.transform.localPosition = new Vector3(0f, height * 0.5f + 0.01f, 0f);
                top.transform.localScale = new Vector3(radius * 1.9f, 0.02f, radius * 1.9f);
                top.GetComponent<Renderer>().sharedMaterial = moss;

                var cylinderMesh = visual.GetComponent<MeshFilter>().sharedMesh;
                // Коллайдер в масштабе визуала: отдельный дочерний объект (плоский верх, без скруглений капсулы).
                var colGo = new GameObject("Collider");
                colGo.transform.SetParent(stone.transform, false);
                colGo.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                var mc = colGo.AddComponent<MeshCollider>();
                mc.sharedMesh = cylinderMesh;
                mc.convex = true;

                var body = stone.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var ps = stone.AddComponent<PondStone>();
                var so = new SerializedObject(ps);
                so.FindProperty("phase").floatValue = i * 15f / 8f * 3f % 15f; // по очереди, но не соседи подряд
                so.ApplyModifiedPropertiesWithoutUndo();

                var point = new GameObject("Top");
                point.transform.SetParent(stone.transform, false);
                point.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                points.Add(point.transform);
            }
            return points;
        }

        private static void InvisibleRing(Transform root, float half)
        {
            foreach (var (center, size) in new[]
            {
                (new Vector3(0f, 2f, half), new Vector3(half * 2f, 4f, 0.2f)),
                (new Vector3(0f, 2f, -half), new Vector3(half * 2f, 4f, 0.2f)),
                (new Vector3(half, 2f, 0f), new Vector3(0.2f, 4f, half * 2f)),
                (new Vector3(-half, 2f, 0f), new Vector3(0.2f, 4f, half * 2f)),
            })
            {
                var go = new GameObject("Invisible");
                go.transform.SetParent(root, false);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }
        }
    }
}
