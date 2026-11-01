using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.WindowHeist;
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
    /// Этап 26: мини-игра «Форточка».
    /// Ночная квартира 16×11 м с потолком: гостиная (диван, журнальный столик, аквариум) и кухня (стол, холодильник, шкафчики).
    /// Посередине на лежанке спит пёс (собран из блоков, анимация процедурная). Снаружи — балкон с открытым окном в квартиру,
    /// там коты появляются и складывают добычу в коробку. 12 вещей добычи (всего 25 очков). NavMesh для пса печётся здесь.
    /// Запуск: Maoshi → Setup → Stage 26 - Window Heist. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage26
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/WindowHeist";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/WindowHeist.unity";
        private const string NavMeshPath = Folder + "/WindowHeist_NavMesh.asset";

        private const float HalfX = 8f;
        private const float BackZ = 5f;
        private const float WallZ = -6f;
        private const float WallHeight = 3.1f;
        private const float WindowHalf = 1.1f;
        private const float WindowBottom = 0.9f;
        private const float WindowTop = 2.0f;

        [MenuItem("Maoshi/Setup/Stage 26 - Window Heist")]
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/WindowHeist.asset");
            definition.EditorSetup("window_heist", "Форточка",
                "Ночью через окно — в квартиру, где спит пёс. Тащи добычу на балкон, крадись (C) и не шуми: проснётся — поймает, выкинет на балкон и отберёт 30% добычи! Золотая рыбка дороже всех, но тяжёлая.",
                "WindowHeist", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            Debug.Log($"[Maoshi Setup] Этап 26 готов: «Форточка» (в каталоге №{index + 1}; во дворе в редакторе — клавиша {(index >= 0 && index < keys.Length ? keys[index] : "—")}).");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, -12f), Quaternion.Euler(15f, 0f, 0f));

            var room = ArtKit.Group("Apartment");
            BuildRoom(room);
            BuildBalcony(room, out var stash);
            var tops = BuildFurniture(room);

            // NavMesh — до пса, котов и добычи (чтобы они не вырезали в нём дыр).
            BakeNavMesh();

            var dog = BuildDog(new Vector3(0f, 0f, 0.6f), -90f);
            var loot = BuildLoot(tops);

            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -8.6f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 0.9f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("WindowMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<WindowMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "window_heist";
            serialized.FindProperty("timeLimitSeconds").floatValue = 100f;
            serialized.FindProperty("dog").objectReferenceValue = dog;
            serialized.FindProperty("stash").objectReferenceValue = stash;
            serialized.FindProperty("wallZ").floatValue = WallZ;
            serialized.FindProperty("insideX").vector2Value = new Vector2(-HalfX, HalfX);
            var lootProperty = serialized.FindProperty("loot");
            lootProperty.arraySize = loot.Count;
            for (int i = 0; i < loot.Count; i++) lootProperty.GetArrayElementAtIndex(i).objectReferenceValue = loot[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 30f, 110f);
            MaoshiSetupStage25.Night(camera);
            // В квартире горит ночник — чуть светлее, чем на улице.
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.38f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.28f, 0.42f);
            DynamicGI.UpdateEnvironment();
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        // ───────────────────────── Квартира ─────────────────────────

        private static void BuildRoom(Transform room)
        {
            var wallpaper = ArtLibrary.Wallpaper;
            var parquet = ArtLibrary.Parquet;
            var trim = ArtKit.Color("Skirting", new Color(0.93f, 0.89f, 0.80f));
            float t = 0.3f;

            ArtKit.UvBox("Floor", new Vector3(0f, -0.3f, (BackZ + WallZ) * 0.5f), new Vector3(HalfX * 2f, 0.3f, BackZ - WallZ), parquet, parquet, new Vector2(1.5f, 1.5f), 1.5f, room);
            ArtKit.UvBox("WallBack", new Vector3(0f, 0f, BackZ + t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);
            float midZ = (BackZ + WallZ) * 0.5f;
            ArtKit.UvBox("WallWest", new Vector3(-HalfX - t * 0.5f, 0f, midZ), new Vector3(t, WallHeight, BackZ - WallZ), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallEast", new Vector3(HalfX + t * 0.5f, 0f, midZ), new Vector3(t, WallHeight, BackZ - WallZ), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);

            // Стена с окном (в сторону балкона): слева, справа, под окном, над окном.
            float wz = WallZ - t * 0.5f;
            float sideWidth = HalfX + t - WindowHalf;
            ArtKit.UvBox("WallFrontL", new Vector3(-WindowHalf - sideWidth * 0.5f, 0f, wz), new Vector3(sideWidth, WallHeight, t), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallFrontR", new Vector3(WindowHalf + sideWidth * 0.5f, 0f, wz), new Vector3(sideWidth, WallHeight, t), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallUnderWindow", new Vector3(0f, 0f, wz), new Vector3(WindowHalf * 2f, WindowBottom, t), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallOverWindow", new Vector3(0f, WindowTop, wz), new Vector3(WindowHalf * 2f, WallHeight - WindowTop, t), wallpaper, trim, new Vector2(1.2f, 1.2f), 1f, room);

            // Подоконники внутри и снаружи — на них удобно запрыгнуть.
            var frame = ArtKit.Color("WindowFrame", new Color(0.96f, 0.95f, 0.92f));
            ArtKit.Box("SillInside", new Vector3(0f, WindowBottom - 0.03f, WallZ + 0.15f), new Vector3(WindowHalf * 2f + 0.3f, 0.06f, 0.36f), frame, room);
            ArtKit.Box("SillOutside", new Vector3(0f, WindowBottom - 0.03f, WallZ - t - 0.15f), new Vector3(WindowHalf * 2f + 0.3f, 0.06f, 0.36f), frame, room);
            ArtKit.Box("FrameL", new Vector3(-WindowHalf, (WindowBottom + WindowTop) * 0.5f, wz), new Vector3(0.08f, WindowTop - WindowBottom, t + 0.04f), frame, room, false);
            ArtKit.Box("FrameR", new Vector3(WindowHalf, (WindowBottom + WindowTop) * 0.5f, wz), new Vector3(0.08f, WindowTop - WindowBottom, t + 0.04f), frame, room, false);
            // Распахнутая створка («форточка»).
            var glass = ArtKit.Color("WindowGlassOpen", new Color(0.70f, 0.82f, 0.92f), 0.7f);
            var sash = ArtKit.Box("OpenSash", new Vector3(-WindowHalf + 0.05f, (WindowBottom + WindowTop) * 0.5f, WallZ + 0.45f), new Vector3(0.9f, WindowTop - WindowBottom - 0.1f, 0.04f), glass, room, false);
            sash.transform.rotation = Quaternion.Euler(0f, -70f, 0f);
            sash.transform.position = new Vector3(-WindowHalf + 0.18f, (WindowBottom + WindowTop) * 0.5f, WallZ + 0.42f);

            // Потолок, плафоны (тусклые — ночь).
            var plaster = ArtKit.Color("CeilingPlaster", new Color(0.96f, 0.94f, 0.89f));
            var ceiling = ArtKit.UvBox("Ceiling", new Vector3(0f, WallHeight, midZ), new Vector3(HalfX * 2f + t * 2f, 0.25f, BackZ - WallZ + t * 2f), plaster, plaster, new Vector2(2f, 2f), 2f, room);
            ceiling.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            MaoshiSetupStage22.Chandelier(room, new Vector3(-4.2f, WallHeight, 1.5f), 1.6f, 0.1f);
            MaoshiSetupStage22.Chandelier(room, new Vector3(4.6f, WallHeight, 1.0f), 1.6f, 0.1f);

            // Картины на стене.
            var gilt = ArtKit.Color("PictureFrame", new Color(0.74f, 0.56f, 0.26f), 0.35f);
            ArtKit.Box("PictureFrame", new Vector3(-4f, 1.9f, BackZ - 0.03f), new Vector3(1.3f, 0.9f, 0.04f), gilt, room, false);
            ArtKit.Box("Picture", new Vector3(-4f, 1.9f, BackZ - 0.06f), new Vector3(1.1f, 0.72f, 0.02f), ArtKit.Color("Picture0", new Color(0.42f, 0.62f, 0.44f)), room, false);
        }

        private static void BuildBalcony(Transform room, out Transform stash)
        {
            var concrete = ArtLibrary.Concrete;
            float z0 = WallZ - 0.3f;
            float depth = 4f;
            ArtKit.UvBox("BalconyFloor", new Vector3(0f, -0.3f, z0 - depth * 0.5f), new Vector3(9f, 0.3f, depth), concrete, concrete, new Vector2(2f, 2f), 2f, room);
            var rail = ArtKit.Color("BalconyRail", new Color(0.32f, 0.36f, 0.42f), 0.4f);
            ArtKit.Box("RailFront", new Vector3(0f, 0.5f, z0 - depth), new Vector3(9f, 1f, 0.12f), rail, room);
            ArtKit.Box("RailL", new Vector3(-4.5f, 0.5f, z0 - depth * 0.5f), new Vector3(0.12f, 1f, depth), rail, room);
            ArtKit.Box("RailR", new Vector3(4.5f, 0.5f, z0 - depth * 0.5f), new Vector3(0.12f, 1f, depth), rail, room);
            foreach (var (center, size) in new[]
            {
                (new Vector3(0f, 2f, z0 - depth), new Vector3(9f, 4f, 0.1f)),
                (new Vector3(-4.5f, 2f, z0 - depth * 0.5f), new Vector3(0.1f, 4f, depth)),
                (new Vector3(4.5f, 2f, z0 - depth * 0.5f), new Vector3(0.1f, 4f, depth)),
            })
            {
                var invisible = new GameObject("BalconyInvisible");
                invisible.transform.SetParent(room, false);
                invisible.transform.position = center;
                invisible.AddComponent<BoxCollider>().size = size;
            }
            // Фасад дома снаружи (видно с балкона) и коробка для добычи.
            // Фасад вокруг окна (с проёмом под само окно).
            float fz = WallZ - 0.31f;
            var facade = ArtLibrary.FacadePanel;
            var tile = new Vector2(6f, ArtLibrary.Floor * 2f);
            ArtKit.UvBox("FacadeL", new Vector3(-7.5f - WindowHalf * 0.5f, -3f, fz), new Vector3(15f - WindowHalf, 9f, 0.02f), facade, facade, tile, 4f, room, false);
            ArtKit.UvBox("FacadeR", new Vector3(7.5f + WindowHalf * 0.5f, -3f, fz), new Vector3(15f - WindowHalf, 9f, 0.02f), facade, facade, tile, 4f, room, false);
            ArtKit.UvBox("FacadeUnder", new Vector3(0f, -3f, fz), new Vector3(WindowHalf * 2f, 3f + WindowBottom, 0.02f), facade, facade, tile, 4f, room, false);
            ArtKit.UvBox("FacadeOver", new Vector3(0f, WindowTop, fz), new Vector3(WindowHalf * 2f, 6f - WindowTop, 0.02f), facade, facade, tile, 4f, room, false);
            ArtKit.PlaceFit("Furniture/cardboardBoxOpen", new Vector3(3.2f, 0f, z0 - 2.8f), 15f, new Vector3(0.9f, 0.6f, 0.9f), room, true);
            var point = new GameObject("Stash");
            point.transform.SetParent(room, false);
            point.transform.position = new Vector3(3.2f, 0.62f, z0 - 2.8f);
            stash = point.transform;
            ArtKit.PlaceFit("Nature/plant_bushDetailed", new Vector3(-3.6f, 0f, z0 - 0.6f), 0f, new Vector3(0.7f, 0.8f, 0.7f), room, true);
        }

        /// <summary>Мебель; возвращает высоты верхов, где лежит добыча.</summary>
        private static Dictionary<string, float> BuildFurniture(Transform room)
        {
            var tops = new Dictionary<string, float>();
            float Top(GameObject go, float fallback) => go != null ? ArtKit.RendererBounds(go).max.y : fallback;

            // Гостиная.
            var sofa = ArtKit.PlaceFit("Furniture/loungeSofa", new Vector3(-5.4f, 0f, 4.2f), 180f, new Vector3(2.8f, 0.95f, 1.1f), room, true);
            tops["sofa"] = Top(sofa, 0.95f);
            ArtKit.PlaceFit("Furniture/rugRound", new Vector3(-4.5f, 0f, 2f), 0f, new Vector3(3f, 0.05f, 3f), room);
            var coffee = ArtKit.PlaceFit("Furniture/tableRound", new Vector3(-4.5f, 0f, 2.2f), 0f, new Vector3(1f, 0.5f, 1f), room, true);
            tops["coffee"] = Top(coffee, 0.5f);
            ArtKit.PlaceFit("Furniture/lampSquareFloor", new Vector3(-7.4f, 0f, 4.5f), 0f, new Vector3(0.5f, 1.7f, 0.5f), room, true);
            var tv = ArtKit.PlaceFit("Furniture/cabinetTelevision", new Vector3(-4.5f, 0f, -5.5f), 0f, new Vector3(2f, 0.6f, 0.5f), room, true);
            ArtKit.PlaceFit("Furniture/televisionVintage", new Vector3(-4.5f, Top(tv, 0.6f), -5.5f), 0f, new Vector3(0.9f, 0.8f, 0.5f), room, true);
            ArtKit.PlaceFit("Furniture/bookcaseOpen", new Vector3(-7.7f, 0f, -3f), 90f, new Vector3(1.6f, 2f, 0.45f), room, true);

            // Аквариум на тумбе: в нём золотая рыбка.
            var stand = ArtKit.Box("AquariumStand", new Vector3(-7.4f, 0.4f, 0.8f), new Vector3(0.7f, 0.8f, 1.4f), ArtLibrary.WoodDark, room);
            stand.name = "AquariumStand";
            var water = ArtKit.Color("AquariumWater", new Color(0.45f, 0.72f, 0.88f), 0.8f);
            ArtKit.Box("Aquarium", new Vector3(-7.45f, 1.05f, 1.15f), new Vector3(0.5f, 0.5f, 0.6f), water, room);
            tops["aquarium"] = 0.8f;

            // Лежанка пса и миска.
            ArtKit.PlaceFit("Furniture/rugRound", new Vector3(0f, 0f, 0.6f), 0f, new Vector3(2.2f, 0.05f, 2.2f), room);
            var cushion = ArtKit.Color("DogCushion", new Color(0.62f, 0.30f, 0.30f));
            ArtKit.Cylinder("DogBed", new Vector3(0f, 0f, 0.6f), 0.75f, 0.12f, cushion, room, false);
            ArtKit.Cylinder("DogBowl", new Vector3(-1.3f, 0f, 1.5f), 0.18f, 0.1f, ArtKit.Color("DogBowl", new Color(0.85f, 0.25f, 0.2f)), room, false);

            // Кухня.
            var table = ArtKit.PlaceFit("Furniture/table", new Vector3(4.8f, 0f, 1f), 90f, new Vector3(1.8f, 0.78f, 1f), room, true);
            tops["table"] = Top(table, 0.78f);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(3.9f, 0f, 1f), 90f, new Vector3(0.55f, 1f, 0.55f), room, true);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(5.7f, 0f, 1f), -90f, new Vector3(0.55f, 1f, 0.55f), room, true);
            ArtKit.PlaceFit("Furniture/kitchenFridge", new Vector3(7.4f, 0f, 4.3f), -90f, new Vector3(0.9f, 2f, 0.8f), room, true);
            float cabinetTop = 0.95f;
            for (int i = 0; i < 4; i++)
            {
                var cab = ArtKit.PlaceFit(i == 1 ? "Furniture/kitchenStove" : "Furniture/kitchenCabinet", new Vector3(7.55f, 0f, 3f - i * 0.95f), -90f, new Vector3(0.95f, 0.95f, 0.75f), room, true);
                if (i == 0) cabinetTop = Top(cab, 0.95f);
            }
            tops["cabinet"] = cabinetTop;
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(7.5f, 0f, -1.8f), 0f, new Vector3(0.45f, 0.6f, 0.45f), room, true);

            // Укрытия посреди комнаты: коробки.
            foreach (var (p, yaw, size) in new[] { (new Vector3(2.6f, 0f, -2.8f), 20f, 0.8f), (new Vector3(-2.2f, 0f, -3.4f), -15f, 0.7f), (new Vector3(-1.6f, 0f, 3.8f), 30f, 0.75f) })
                ArtKit.PlaceFit("Furniture/cardboardBoxClosed", p, yaw, new Vector3(size, size * 0.8f, size), room, true);
            return tops;
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

        // ───────────────────────── Пёс ─────────────────────────

        private static Dog BuildDog(Vector3 position, float yaw)
        {
            var tan = ArtKit.Color("DogTan", new Color(0.80f, 0.58f, 0.34f));
            var light = ArtKit.Color("DogLight", new Color(0.95f, 0.86f, 0.70f));
            var dark = ArtKit.Color("DogDark", new Color(0.36f, 0.22f, 0.14f));
            var black = ArtKit.Color("DogNose", new Color(0.08f, 0.07f, 0.07f), 0.6f);
            var collar = ArtKit.Color("DogCollar", new Color(0.85f, 0.2f, 0.2f));

            var root = new GameObject("Dog");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var offset = new GameObject("BodyOffset").transform;
            offset.SetParent(root.transform, false);
            offset.localPosition = new Vector3(0f, 0.52f, 0f);
            var body = new GameObject("Body").transform;
            body.SetParent(offset, false);

            Block(body, "Torso", new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.42f, 0.95f), tan);
            Block(body, "Belly", new Vector3(0f, -0.15f, 0.05f), new Vector3(0.42f, 0.14f, 0.7f), light);
            Block(body, "Collar", new Vector3(0f, 0.12f, 0.43f), new Vector3(0.44f, 0.1f, 0.12f), collar);

            var head = new GameObject("Head").transform;
            head.SetParent(body, false);
            head.localPosition = new Vector3(0f, 0.2f, 0.5f);
            Block(head, "Skull", new Vector3(0f, 0.12f, 0.12f), new Vector3(0.42f, 0.38f, 0.42f), tan);
            Block(head, "Snout", new Vector3(0f, 0.03f, 0.38f), new Vector3(0.24f, 0.18f, 0.26f), light);
            Block(head, "Nose", new Vector3(0f, 0.1f, 0.51f), new Vector3(0.09f, 0.07f, 0.05f), black);
            Block(head, "EyeL", new Vector3(-0.11f, 0.2f, 0.33f), new Vector3(0.06f, 0.03f, 0.02f), black);
            Block(head, "EyeR", new Vector3(0.11f, 0.2f, 0.33f), new Vector3(0.06f, 0.03f, 0.02f), black);
            var earL = Pivot(head, "EarL", new Vector3(-0.2f, 0.3f, 0.08f));
            Block(earL, "Flap", new Vector3(0f, -0.13f, 0f), new Vector3(0.08f, 0.28f, 0.17f), dark);
            var earR = Pivot(head, "EarR", new Vector3(0.2f, 0.3f, 0.08f));
            Block(earR, "Flap", new Vector3(0f, -0.13f, 0f), new Vector3(0.08f, 0.28f, 0.17f), dark);

            var legs = new Transform[4];
            Vector3[] legPos = { new(-0.17f, -0.12f, 0.33f), new(0.17f, -0.12f, 0.33f), new(-0.17f, -0.12f, -0.33f), new(0.17f, -0.12f, -0.33f) };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = Pivot(body, $"Leg{i}", legPos[i]);
                Block(legs[i], "Leg", new Vector3(0f, -0.19f, 0f), new Vector3(0.14f, 0.38f, 0.14f), tan);
                Block(legs[i], "Paw", new Vector3(0f, -0.37f, 0.03f), new Vector3(0.16f, 0.07f, 0.2f), light);
            }
            var tail = Pivot(body, "Tail", new Vector3(0f, 0.12f, -0.47f));
            Block(tail, "TailBlock", new Vector3(0f, 0f, -0.17f), new Vector3(0.09f, 0.09f, 0.36f), dark);

            var signGo = new GameObject("Sign");
            signGo.transform.SetParent(root.transform, false);
            var sign = signGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.font = font;
            sign.fontSize = 80;
            sign.characterSize = 0.05f;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.fontStyle = FontStyle.Bold;
            signGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.center = new Vector3(0f, 0.45f, 0f);
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 0.9f;
            agent.angularSpeed = 600f;
            agent.acceleration = 25f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;

            var dog = root.AddComponent<Dog>();
            var so = new SerializedObject(dog);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("tail").objectReferenceValue = tail;
            var legsProperty = so.FindProperty("legs");
            legsProperty.arraySize = 4;
            for (int i = 0; i < 4; i++) legsProperty.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            so.FindProperty("earL").objectReferenceValue = earL;
            so.FindProperty("earR").objectReferenceValue = earR;
            so.FindProperty("sign").objectReferenceValue = sign;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            return dog;
        }

        private static Transform Pivot(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name).transform;
            go.SetParent(parent, false);
            go.localPosition = local;
            return go;
        }

        private static void Block(Transform parent, string name, Vector3 local, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        // ───────────────────────── Добыча ─────────────────────────

        private static List<Loot> BuildLoot(Dictionary<string, float> tops)
        {
            var list = new List<Loot>();
            void Add(string kind, Vector3 p)
            {
                var loot = MakeLoot(kind, p, list.Count);
                if (loot != null) list.Add(loot);
            }
            Add("sausage", new Vector3(4.6f, tops["table"], 1.4f));
            Add("sausage", new Vector3(6.2f, 0f, 3.9f));
            Add("sausage", new Vector3(-5.6f, tops["sofa"] * 0.55f, 4.1f));
            Add("sausage", new Vector3(3.2f, 0f, -1.6f));
            Add("fish", new Vector3(7.55f, tops["cabinet"], 2.9f));
            Add("fish", new Vector3(1.2f, 0f, 1.7f));
            Add("fish", new Vector3(5f, tops["table"], 0.5f));
            Add("can", new Vector3(7.55f, tops["cabinet"], 0.2f));
            Add("can", new Vector3(-3.4f, 0f, -3.2f));
            Add("doctor", new Vector3(-4.5f, tops["coffee"], 2.2f));
            Add("doctor", new Vector3(-1.4f, 0f, 1.9f));
            Add("goldfish", new Vector3(-7.4f, tops["aquarium"], 0.35f));
            return list;
        }

        private static Loot MakeLoot(string kind, Vector3 position, int index)
        {
            string model; Vector3 size; int value; int weight; string title; Color? tint = null;
            switch (kind)
            {
                case "fish": model = "Food/fish"; size = new Vector3(0.45f, 0.2f, 0.45f); value = 2; weight = 0; title = "Рыба"; break;
                case "can": model = "Food/can"; size = new Vector3(0.22f, 0.26f, 0.22f); value = 2; weight = 1; title = "Консерва"; break;
                case "doctor": model = "Food/sausage"; size = new Vector3(0.6f, 0.6f, 0.6f); value = 3; weight = 1; title = "«Докторская»"; tint = new Color(0.93f, 0.58f, 0.58f); break;
                case "goldfish": model = "Food/fish"; size = new Vector3(0.55f, 0.25f, 0.55f); value = 5; weight = 2; title = "Золотая рыбка"; tint = new Color(1f, 0.72f, 0.15f); break;
                default: model = "Food/sausage"; size = new Vector3(0.32f, 0.32f, 0.32f); value = 1; weight = 0; title = "Сосиска"; break;
            }

            var root = new GameObject($"Loot{index}_{kind}");
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, index * 53f, 0f);
            var placed = ArtKit.PlaceFit(model, position, index * 53f, size, root.transform);
            Transform visual = placed != null ? placed.transform : null;
            if (visual == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = Vector3.up * 0.1f;
                cube.transform.localScale = new Vector3(0.12f, 0.15f, 0.12f);
                visual = cube.transform;
            }
            if (tint.HasValue)
            {
                var material = ArtKit.Color($"Loot_{kind}", tint.Value, 0.5f);
                if (kind == "goldfish")
                {
                    material.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.02f));
                    material.EnableKeyword("_EMISSION");
                }
                foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = material;
                    r.sharedMaterials = ms;
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            var bounds = ArtKit.RendererBounds(visual.gameObject);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(Mathf.Max(0.3f, bounds.size.x), Mathf.Max(0.25f, bounds.size.y), Mathf.Max(0.3f, bounds.size.z));
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            root.AddComponent<NetworkObject>();
            var loot = root.AddComponent<Loot>();
            var so = new SerializedObject(loot);
            so.FindProperty("value").intValue = value;
            so.FindProperty("weight").intValue = weight;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("model").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return loot;
        }
    }
}
