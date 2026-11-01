using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.WhoBrokeVase;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 22: мини-игра «Кто опрокинул вазу».
    /// Бабушкина квартира 18×13 м с потолком и люстрами: паркет, обои, окно со шторами, диван, столы, комод, кухонный угол, телевизор.
    /// 10 ваз (точёные меши 4 форм) на столах, комоде, тумбах и на полу. Звуки — clink / wind / vase_break (этап 21 настраивает импорт).
    /// Запуск: Maoshi → Setup → Stage 22 - Who Broke The Vase. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage22
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/WhoBrokeVase";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/WhoBrokeVase.unity";
        internal const string MeshFolder = ArtKit.MeshesFolder + "/Vases";

        private const float HalfX = 9f;
        private const float HalfZ = 6.5f;
        private const float WallHeight = 3.2f;

        private enum Shape { Amphora, Tall, Round, Floor }

        [MenuItem("Maoshi/Setup/Stage 22 - Who Broke The Vase")]
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
            MaoshiSetup.EnsureFolder(MeshFolder);

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/WhoBrokeVase.asset");
            definition.EditorSetup("who_broke_vase", "Кто опрокинул вазу",
                "Бабушка спит, а кто-то из котов тайно роняет её вазы. Лови качающиеся вазы лапой, а потом вычисли пакостника! Или стань им сам и не попадись.",
                "WhoBrokeVase", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 22 готов: «Кто опрокинул вазу» (в каталоге №{index + 1}; во дворе в редакторе — клавиша {(index < 9 ? (index + 1).ToString() : "0")}).");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 13f, -14f), Quaternion.Euler(45f, 0f, 0f));

            var room = ArtKit.Group("Apartment");
            BuildRoom(room);
            var spots = BuildFurniture(room);

            // Вазы — в корне сцены (сетевые объекты).
            var vases = new List<Vase>();
            var colors = new[]
            {
                new Color(0.27f, 0.47f, 0.82f), new Color(0.90f, 0.88f, 0.82f), new Color(0.80f, 0.36f, 0.24f),
                new Color(0.32f, 0.62f, 0.48f), new Color(0.92f, 0.72f, 0.28f), new Color(0.60f, 0.38f, 0.66f),
            };
            var bands = new[] { new Color(0.96f, 0.94f, 0.88f), new Color(0.22f, 0.36f, 0.72f), new Color(0.98f, 0.82f, 0.36f) };
            for (int i = 0; i < spots.Count; i++)
            {
                var (position, shape) = spots[i];
                vases.Add(CreateVase($"Vase{i}", position, shape, colors[i % colors.Length], bands[i % bands.Length], i));
            }

            // Коты по кругу вокруг круглого стола.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.position = new Vector3(0f, 0f, -0.4f);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Circle;
            spawnerSerialized.FindProperty("spawnRadius").floatValue = 3.1f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Правила.
            var modeGo = new GameObject("VaseMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<VaseMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "who_broke_vase";
            serialized.FindProperty("timeLimitSeconds").floatValue = 0f;
            var vasesProperty = serialized.FindProperty("vases");
            vasesProperty.arraySize = vases.Count;
            for (int i = 0; i < vases.Count; i++) vasesProperty.GetArrayElementAtIndex(i).objectReferenceValue = vases[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 30f, 110f);
            ConvertSceneMaterials();
            SaveWithNetworkIds(scene, ScenePath);
        }

        // ───────────────────────── Комната ─────────────────────────

        private static void BuildRoom(Transform room)
        {
            var wallpaper = ArtLibrary.Wallpaper;
            var parquet = ArtLibrary.Parquet;
            var skirting = ArtKit.Color("Skirting", new Color(0.93f, 0.89f, 0.80f));

            // Пол и подложка под ним (за стенами — тоже пол, чтобы сверху не было дыр).
            ArtKit.UvBox("Floor", new Vector3(0f, -0.3f, 0f), new Vector3(HalfX * 2f, 0.3f, HalfZ * 2f), parquet, parquet, new Vector2(1.5f, 1.5f), 1.5f, room);
            ArtKit.Box("Underfloor", new Vector3(0f, -0.8f, -2f), new Vector3(60f, 1f, 50f), ArtKit.Color("Courtyard", new Color(0.36f, 0.42f, 0.34f)), room);

            // Стены: четыре, высотой до потолка.
            float t = 0.3f;
            ArtKit.UvBox("WallNorth", new Vector3(0f, 0f, HalfZ + t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, skirting, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallWest", new Vector3(-HalfX - t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, skirting, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallEast", new Vector3(HalfX + t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, skirting, new Vector2(1.2f, 1.2f), 1f, room);
            ArtKit.UvBox("WallSouth", new Vector3(0f, 0f, -HalfZ - t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, skirting, new Vector2(1.2f, 1.2f), 1f, room);

            // Потолок: побелка, лепной бордюр, люстра. Тень от него не ложится — иначе в комнате ночь.
            var plaster = ArtKit.Color("CeilingPlaster", new Color(0.96f, 0.94f, 0.89f));
            var ceiling = ArtKit.UvBox("Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(HalfX * 2f + t * 2f, 0.25f, HalfZ * 2f + t * 2f), plaster, plaster, new Vector2(2f, 2f), 2f, room);
            ceiling.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtKit.Box("CorniceN", new Vector3(0f, WallHeight - 0.06f, HalfZ - 0.05f), new Vector3(HalfX * 2f, 0.12f, 0.1f), skirting, room, false);
            ArtKit.Box("CorniceS", new Vector3(0f, WallHeight - 0.06f, -HalfZ + 0.05f), new Vector3(HalfX * 2f, 0.12f, 0.1f), skirting, room, false);
            ArtKit.Box("CorniceW", new Vector3(-HalfX + 0.05f, WallHeight - 0.06f, 0f), new Vector3(0.1f, 0.12f, HalfZ * 2f), skirting, room, false);
            ArtKit.Box("CorniceE", new Vector3(HalfX - 0.05f, WallHeight - 0.06f, 0f), new Vector3(0.1f, 0.12f, HalfZ * 2f), skirting, room, false);
            Chandelier(room, new Vector3(0f, WallHeight, 0.6f), 3.2f);
            Chandelier(room, new Vector3(-5.2f, WallHeight, -1.6f), 2.4f);
            Chandelier(room, new Vector3(5.6f, WallHeight, -3.2f), 2.4f);

            // Дверь в прихожую на ближней стене.
            var doorWood = ArtKit.Color("DoorWood", new Color(0.55f, 0.36f, 0.22f));
            float dz = -HalfZ + 0.02f;
            ArtKit.Box("Door", new Vector3(-2.6f, 1.05f, dz + 0.02f), new Vector3(1.0f, 2.1f, 0.05f), doorWood, room, false);
            ArtKit.Box("DoorFrameL", new Vector3(-3.15f, 1.1f, dz + 0.04f), new Vector3(0.1f, 2.2f, 0.08f), skirting, room, false);
            ArtKit.Box("DoorFrameR", new Vector3(-2.05f, 1.1f, dz + 0.04f), new Vector3(0.1f, 2.2f, 0.08f), skirting, room, false);
            ArtKit.Box("DoorFrameTop", new Vector3(-2.6f, 2.2f, dz + 0.04f), new Vector3(1.2f, 0.1f, 0.08f), skirting, room, false);
            ArtKit.Box("DoorHandle", new Vector3(-2.25f, 1.0f, dz + 0.07f), new Vector3(0.12f, 0.04f, 0.05f), ArtKit.Color("Brass", new Color(0.86f, 0.70f, 0.32f), 0.5f), room, false);
            ArtKit.Box("SkirtS", new Vector3(0f, 0.06f, -HalfZ + 0.02f), new Vector3(HalfX * 2f, 0.12f, 0.04f), skirting, room, false);
            // Ковёр-«ковёр на стене» — куда же без него у бабушки.
            ArtKit.Box("WallCarpet", new Vector3(3.2f, 1.7f, dz + 0.02f), new Vector3(2.6f, 1.6f, 0.03f), ArtKit.Color("WallCarpet", new Color(0.62f, 0.18f, 0.20f)), room, false);
            ArtKit.Box("WallCarpetInner", new Vector3(3.2f, 1.7f, dz + 0.04f), new Vector3(2.0f, 1.1f, 0.02f), ArtKit.Color("WallCarpetInner", new Color(0.86f, 0.62f, 0.30f)), room, false);
            ArtKit.Box("WallCarpetCenter", new Vector3(3.2f, 1.7f, dz + 0.055f), new Vector3(0.9f, 0.5f, 0.02f), ArtKit.Color("WallCarpetCenter", new Color(0.20f, 0.34f, 0.52f)), room, false);

            // Плинтусы.
            ArtKit.Box("SkirtN", new Vector3(0f, 0.06f, HalfZ - 0.02f), new Vector3(HalfX * 2f, 0.12f, 0.04f), skirting, room, false);
            ArtKit.Box("SkirtW", new Vector3(-HalfX + 0.02f, 0.06f, 0f), new Vector3(0.04f, 0.12f, HalfZ * 2f), skirting, room, false);
            ArtKit.Box("SkirtE", new Vector3(HalfX - 0.02f, 0.06f, 0f), new Vector3(0.04f, 0.12f, HalfZ * 2f), skirting, room, false);

            // Окно со шторами и подоконником на дальней стене.
            var sky = ArtKit.Color("WindowSky", new Color(0.62f, 0.78f, 0.96f));
            sky.SetColor("_EmissionColor", new Color(0.30f, 0.40f, 0.52f));
            sky.EnableKeyword("_EMISSION");
            var frame = ArtKit.Color("WindowFrame", new Color(0.96f, 0.95f, 0.92f));
            var curtain = ArtKit.Color("Curtain", new Color(0.78f, 0.30f, 0.30f));
            float wz = HalfZ - 0.02f;
            foreach (float wx in new[] { -2.4f, 2.4f })
            {
                ArtKit.Quad("WindowGlass", new Vector3(wx, 1.75f, wz), new Vector2(1.7f, 1.4f), sky, room);
                ArtKit.Box("FrameTop", new Vector3(wx, 2.47f, wz - 0.03f), new Vector3(1.9f, 0.1f, 0.08f), frame, room, false);
                ArtKit.Box("FrameBottom", new Vector3(wx, 1.03f, wz - 0.03f), new Vector3(1.9f, 0.1f, 0.08f), frame, room, false);
                ArtKit.Box("FrameMid", new Vector3(wx, 1.75f, wz - 0.03f), new Vector3(0.07f, 1.4f, 0.08f), frame, room, false);
                ArtKit.Box("FrameCross", new Vector3(wx, 1.95f, wz - 0.03f), new Vector3(1.7f, 0.06f, 0.08f), frame, room, false);
                ArtKit.Box("FrameL", new Vector3(wx - 0.9f, 1.75f, wz - 0.03f), new Vector3(0.1f, 1.5f, 0.08f), frame, room, false);
                ArtKit.Box("FrameR", new Vector3(wx + 0.9f, 1.75f, wz - 0.03f), new Vector3(0.1f, 1.5f, 0.08f), frame, room, false);
                ArtKit.Box("Sill", new Vector3(wx, 0.96f, wz - 0.14f), new Vector3(2.1f, 0.06f, 0.3f), frame, room, false);
                ArtKit.Box("CurtainL", new Vector3(wx - 1.15f, 1.6f, wz - 0.12f), new Vector3(0.45f, 1.9f, 0.08f), curtain, room, false);
                ArtKit.Box("CurtainR", new Vector3(wx + 1.15f, 1.6f, wz - 0.12f), new Vector3(0.45f, 1.9f, 0.08f), curtain, room, false);
                ArtKit.Box("Rod", new Vector3(wx, 2.6f, wz - 0.12f), new Vector3(2.9f, 0.05f, 0.05f), ArtLibrary.WoodDark, room, false);
                ArtKit.PlaceFit("Furniture/plantSmall1", new Vector3(wx + 0.6f, 0.99f, wz - 0.14f), 0f, new Vector3(0.25f, 0.35f, 0.25f), room);
            }

            // Картины и часы на стенах.
            var picture = new[] { new Color(0.42f, 0.62f, 0.44f), new Color(0.88f, 0.64f, 0.36f), new Color(0.46f, 0.56f, 0.80f) };
            var gilt = ArtKit.Color("PictureFrame", new Color(0.74f, 0.56f, 0.26f), 0.35f);
            for (int i = 0; i < 3; i++)
            {
                float z = -3.4f + i * 2.4f;
                ArtKit.Box("PictureFrame", new Vector3(-HalfX + 0.03f, 1.9f, z), new Vector3(0.04f, 0.9f, 1.1f), gilt, room, false);
                ArtKit.Box("Picture", new Vector3(-HalfX + 0.06f, 1.9f, z), new Vector3(0.02f, 0.72f, 0.92f), ArtKit.Color($"Picture{i}", picture[i]), room, false);
            }
            ArtKit.Cylinder("Clock", new Vector3(HalfX - 0.04f, 2.0f, -1.6f), 0.32f, 0.05f, frame, room, false).transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            ArtKit.Cylinder("ClockRim", new Vector3(HalfX - 0.03f, 2.0f, -1.6f), 0.36f, 0.04f, ArtLibrary.WoodDark, room, false).transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>Люстра: тёплый свет под потолком (солнце через потолок не проходит).</summary>
        internal static void Chandelier(Transform room, Vector3 ceilingPoint, float scale, float drop = 0.55f)
        {
            var brass = ArtKit.Color("Brass", new Color(0.86f, 0.70f, 0.32f), 0.5f);
            var shade = ArtKit.Color("LampShade", new Color(1f, 0.92f, 0.72f));
            shade.SetColor("_EmissionColor", new Color(0.9f, 0.7f, 0.4f));
            shade.EnableKeyword("_EMISSION");
            ArtKit.Cylinder("ChandelierRod", ceilingPoint + Vector3.down * drop, 0.025f, drop, brass, room, false);
            ArtKit.Cylinder("ChandelierShade", ceilingPoint + Vector3.down * (drop + 0.3f), 0.32f, 0.32f, shade, room, false);
            var go = new GameObject("ChandelierLight");
            go.transform.SetParent(room, false);
            go.transform.position = ceilingPoint + Vector3.down * (drop + 0.4f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.86f, 0.66f);
            light.range = 4.5f * scale;
            light.intensity = 1.1f + scale * 0.25f;
            light.shadows = LightShadows.None;
        }

        // ───────────────────────── Мебель и места для ваз ─────────────────────────

        private static float TopOf(GameObject go, float fallback) => go != null ? ArtKit.RendererBounds(go).max.y : fallback;

        private static List<(Vector3, Shape)> BuildFurniture(Transform room)
        {
            var spots = new List<(Vector3, Shape)>();

            // Ковёр и круглый стол в центре.
            ArtKit.PlaceFit("Furniture/rugRound", new Vector3(0f, 0f, 0.6f), 0f, new Vector3(4.2f, 0.05f, 4.2f), room);
            var roundTable = ArtKit.PlaceFit("Furniture/tableRound", new Vector3(0f, 0f, 0.6f), 0f, new Vector3(1.4f, 0.78f, 1.4f), room, true);
            spots.Add((new Vector3(0f, TopOf(roundTable, 0.78f), 0.6f), Shape.Round));
            ArtKit.PlaceFit("Furniture/chair", new Vector3(1.25f, 0f, 1.1f), -110f, new Vector3(0.6f, 1.0f, 0.6f), room, true);

            // Диван у дальней стены, рядом торшер и напольная ваза.
            ArtKit.PlaceFit("Furniture/loungeSofa", new Vector3(-5.6f, 0f, 5.6f), 180f, new Vector3(3.0f, 1.0f, 1.2f), room, true);
            ArtKit.PlaceFit("Furniture/lampSquareFloor", new Vector3(-3.6f, 0f, 6.0f), 0f, new Vector3(0.5f, 1.7f, 0.5f), room, true);
            var lamp = new GameObject("LampLight");
            lamp.transform.SetParent(room, false);
            lamp.transform.position = new Vector3(-3.6f, 1.6f, 5.8f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.80f, 0.55f);
            light.range = 6f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            spots.Add((new Vector3(-7.9f, 0f, 5.7f), Shape.Floor));
            spots.Add((new Vector3(0f, 0f, 5.85f), Shape.Floor));

            // Тумба с телевизором.
            var tvCabinet = ArtKit.PlaceFit("Furniture/cabinetTelevision", new Vector3(5.6f, 0f, 5.95f), 180f, new Vector3(2.2f, 0.7f, 0.6f), room, true);
            float tvTop = TopOf(tvCabinet, 0.6f);
            ArtKit.PlaceFit("Furniture/televisionVintage", new Vector3(5.2f, tvTop, 5.95f), 180f, new Vector3(0.9f, 0.8f, 0.55f), room, true);
            spots.Add((new Vector3(6.25f, tvTop, 5.95f), Shape.Tall));
            ArtKit.PlaceFit("Furniture/pottedPlant", new Vector3(3.8f, 0f, 6.0f), 0f, new Vector3(0.6f, 1.2f, 0.6f), room, true);

            // Обеденный стол с двумя вазами.
            var table = ArtKit.PlaceFit("Furniture/table", new Vector3(-5.2f, 0f, -1.6f), 0f, new Vector3(2.4f, 0.8f, 1.1f), room, true);
            float tableTop = TopOf(table, 0.8f);
            spots.Add((new Vector3(-6.0f, tableTop, -1.6f), Shape.Amphora));
            spots.Add((new Vector3(-4.4f, tableTop, -1.6f), Shape.Tall));
            ArtKit.PlaceFit("Furniture/chair", new Vector3(-5.6f, 0f, -0.55f), 180f, new Vector3(0.6f, 1.0f, 0.6f), room, true);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(-4.6f, 0f, -2.7f), 0f, new Vector3(0.6f, 1.0f, 0.6f), room, true);

            // Бабушкин комод у левой стены.
            var wood = ArtLibrary.Wood;
            var woodDark = ArtLibrary.WoodDark;
            ArtKit.Box("Commode", new Vector3(-HalfX + 0.32f, 0.43f, -4.6f), new Vector3(0.6f, 0.86f, 1.7f), wood, room);
            for (int i = 0; i < 3; i++)
            {
                ArtKit.Box("Drawer", new Vector3(-HalfX + 0.63f, 0.18f + i * 0.26f, -4.6f), new Vector3(0.03f, 0.2f, 1.55f), woodDark, room, false);
                ArtKit.Box("Knob", new Vector3(-HalfX + 0.66f, 0.18f + i * 0.26f, -4.6f), new Vector3(0.04f, 0.05f, 0.12f), ArtKit.Color("Brass", new Color(0.86f, 0.70f, 0.32f), 0.5f), room, false);
            }
            spots.Add((new Vector3(-HalfX + 0.32f, 0.86f, -4.6f), Shape.Amphora));

            // Кухонный угол.
            var cabinet = ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(5.3f, 0f, -5.95f), 0f, new Vector3(1.0f, 0.95f, 0.7f), room, true);
            float cabinetTop = TopOf(cabinet, 0.9f);
            ArtKit.PlaceFit("Furniture/kitchenStove", new Vector3(6.35f, 0f, -5.95f), 0f, new Vector3(1.0f, 0.95f, 0.7f), room, true);
            var cabinet2 = ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(7.4f, 0f, -5.95f), 0f, new Vector3(1.0f, 0.95f, 0.7f), room, true);
            ArtKit.PlaceFit("Furniture/kitchenFridge", new Vector3(8.45f, 0f, -5.85f), 0f, new Vector3(0.9f, 2.0f, 0.8f), room, true);
            ArtKit.PlaceFit("Furniture/radio", new Vector3(7.5f, TopOf(cabinet2, 0.9f), -5.95f), 0f, new Vector3(0.4f, 0.3f, 0.25f), room);
            spots.Add((new Vector3(5.3f, cabinetTop, -5.95f), Shape.Round));

            // Книжный шкаф у правой стены и ваза рядом на полу.
            ArtKit.PlaceFit("Furniture/bookcaseOpen", new Vector3(HalfX - 0.3f, 0f, 2.2f), -90f, new Vector3(1.6f, 2.0f, 0.5f), room, true);
            spots.Add((new Vector3(HalfX - 0.45f, 0f, -0.6f), Shape.Floor));

            // Скамеечка у двери — ещё одна ваза на виду.
            var bench = ArtKit.PlaceFit("Furniture/bench", new Vector3(1.8f, 0f, -5.7f), 0f, new Vector3(1.4f, 0.5f, 0.5f), room, true);
            spots.Add((new Vector3(1.5f, TopOf(bench, 0.45f), -5.7f), Shape.Tall));

            return spots;
        }

        // ───────────────────────── Вазы ─────────────────────────

        private static Vase CreateVase(string name, Vector3 position, Shape shape, Color color, Color bandColor, int index)
        {
            var profile = Profile(shape);
            float height = 0f, radius = 0f, widestY = 0f;
            foreach (var p in profile)
            {
                height = Mathf.Max(height, p.y);
                if (p.x > radius) { radius = p.x; widestY = p.y; }
            }

            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, index * 47f, 0f));

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = LatheMesh($"Vase_{shape}", profile);
            model.AddComponent<MeshRenderer>().sharedMaterial = ArtKit.Color($"Vase{index % 6}", color, 0.5f);

            // Поясок другого цвета по самому широкому месту.
            var band = new GameObject("Band");
            band.transform.SetParent(model.transform, false);
            band.AddComponent<MeshFilter>().sharedMesh = LatheMesh($"VaseBand_{shape}", new[]
            {
                new Vector2(BandRadius(profile, widestY - 0.035f), widestY - 0.035f),
                new Vector2(BandRadius(profile, widestY) , widestY),
                new Vector2(BandRadius(profile, widestY + 0.035f), widestY + 0.035f),
            }, 0.006f, false);
            band.AddComponent<MeshRenderer>().sharedMaterial = ArtKit.Color($"VaseBand{index % 3}", bandColor, 0.5f);

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(radius * 2f, height, radius * 2f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            root.AddComponent<NetworkObject>();
            var vase = root.AddComponent<Vase>();
            var serialized = new SerializedObject(vase);
            serialized.FindProperty("model").objectReferenceValue = model.transform;
            serialized.FindProperty("shardColor").colorValue = color;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return vase;
        }

        /// <summary>Профиль вазы (радиус, высота): снаружи снизу вверх до края, потом внутрь горлышка вниз.</summary>
        private static Vector2[] Profile(Shape shape) => shape switch
        {
            Shape.Amphora => new[]
            {
                new Vector2(0.10f, 0f), new Vector2(0.15f, 0.04f), new Vector2(0.21f, 0.15f), new Vector2(0.225f, 0.26f), new Vector2(0.19f, 0.38f),
                new Vector2(0.11f, 0.48f), new Vector2(0.085f, 0.55f), new Vector2(0.11f, 0.60f), new Vector2(0.09f, 0.605f), new Vector2(0.065f, 0.53f),
            },
            Shape.Tall => new[]
            {
                new Vector2(0.08f, 0f), new Vector2(0.11f, 0.04f), new Vector2(0.14f, 0.24f), new Vector2(0.125f, 0.44f), new Vector2(0.075f, 0.60f),
                new Vector2(0.068f, 0.66f), new Vector2(0.095f, 0.71f), new Vector2(0.078f, 0.715f), new Vector2(0.05f, 0.62f),
            },
            Shape.Round => new[]
            {
                new Vector2(0.09f, 0f), new Vector2(0.17f, 0.05f), new Vector2(0.235f, 0.17f), new Vector2(0.22f, 0.29f), new Vector2(0.14f, 0.37f),
                new Vector2(0.12f, 0.40f), new Vector2(0.135f, 0.43f), new Vector2(0.11f, 0.435f), new Vector2(0.085f, 0.37f),
            },
            _ => new[]
            {
                new Vector2(0.15f, 0f), new Vector2(0.20f, 0.06f), new Vector2(0.28f, 0.38f), new Vector2(0.25f, 0.70f), new Vector2(0.15f, 0.92f),
                new Vector2(0.13f, 1.00f), new Vector2(0.175f, 1.07f), new Vector2(0.15f, 1.075f), new Vector2(0.10f, 0.97f),
            },
        };

        private static float BandRadius(Vector2[] profile, float y)
        {
            // Радиус внешней стороны на высоте y (до края горлышка).
            for (int i = 0; i < profile.Length - 1; i++)
            {
                var a = profile[i];
                var b = profile[i + 1];
                if (b.y < a.y) break;
                if (y >= a.y && y <= b.y) return Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, y)) + 0.004f;
            }
            return profile[0].x;
        }

        /// <summary>Тело вращения по профилю. Нормали — по наклону профиля (гладко). Дно — диск.</summary>
        internal static Mesh LatheMesh(string assetName, Vector2[] profile, float grow = 0f, bool bottom = true)
        {
            const int segments = 32;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int rings = profile.Length;

            for (int r = 0; r < rings; r++)
            {
                Vector2 prev = profile[Mathf.Max(0, r - 1)];
                Vector2 next = profile[Mathf.Min(rings - 1, r + 1)];
                Vector2 tangent = (next - prev).normalized;
                Vector2 n2 = new Vector2(tangent.y, -tangent.x); // наружу при движении вверх
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vertices.Add(dir * (profile[r].x + grow) + Vector3.up * profile[r].y);
                    normals.Add((dir * n2.x + Vector3.up * n2.y).normalized);
                    uvs.Add(new Vector2(s / (float)segments, r / (float)(rings - 1)));
                }
            }

            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int i0 = r * (segments + 1) + s;
                    int i1 = i0 + 1;
                    int i2 = i0 + segments + 1;
                    int i3 = i2 + 1;
                    triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }
            }

            if (bottom)
            {
                int center = vertices.Count;
                vertices.Add(Vector3.up * 0.001f);
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f, 0.5f));
                int start = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(a) * profile[0].x, 0.001f, Mathf.Sin(a) * profile[0].x));
                    normals.Add(Vector3.down);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int s = 0; s < segments; s++) triangles.AddRange(new[] { center, start + s, start + s + 1 });
            }

            string path = $"{MeshFolder}/{assetName}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = assetName;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ───────────────────────── Общее ─────────────────────────

        /// <summary>Новые материалы (Lit) — в мультяшный шейдер, как всё остальное в игре.</summary>
        internal static void ConvertSceneMaterials()
        {
            var toon = Shader.Find(MaoshiSetupStage18.ToonShader);
            if (toon == null) return;
            var done = new HashSet<Material>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !done.Add(material) || !MaoshiSetupStage18.IsLit(material.shader)) continue;
                    if (!AssetDatabase.GetAssetPath(material).StartsWith("Assets/_Maoshi")) continue;
                    MaoshiSetupStage18.ConvertMaterial(material, toon);
                }
            }
        }

        internal static void SaveWithNetworkIds(Scene scene, string path)
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
