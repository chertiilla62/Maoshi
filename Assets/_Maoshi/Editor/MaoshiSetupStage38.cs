using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.SunSpot;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 38: мини-игра «Солнечное пятно» (заменяет «Сумо на подоконнике»).
    /// Гостиная 14×10 м на втором этаже: большое окно на северной стене с широким подоконником, диван, кресло,
    /// пуф, журнальный столик (под ним тень), кошачий домик-когтеточка, лежанка, книжный шкаф, торшер.
    /// Пятно солнца ходит по маршруту через подоконник, пол, кресло, столик, когтеточку, диван и пуф.
    /// Хозяйка с пульверизатором стоит в дверях (восточная стена).
    /// Каталог: «Сумо» заменяется «Солнечным пятном» на том же месте (та же клавиша отладки во дворе — 5),
    /// сцена сумо убирается из сборки.
    /// Запуск: Maoshi → Setup → Stage 38 - Sun Spot. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage38
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/SunSpot";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/SunSpot.unity";
        private const string OldDefinition = MaoshiSetupStage3.MiniGamesFolder + "/SumoSill/SumoSill.asset";
        private const string OldScene = MaoshiSetup.ScenesFolder + "/SumoSill.unity";
        private const string HostessController = ArtKit.ArtFolder + "/Animators/AC_Hostess.controller";

        private const float HalfX = 7f;
        private const float HalfZ = 5f;
        private const float WallHeight = 3.1f;
        private const float SillTop = 0.9f;
        private const float WindowTop = 2.9f;
        private const float WindowHalf = 3f;

        [MenuItem("Maoshi/Setup/Stage 38 - Sun Spot")]
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
            MaoshiSetup.EnsureFolder(MaoshiSetupStage22.MeshFolder);

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/SunSpot.asset");
            definition.EditorSetup("sun_spot", "Солнечное пятно",
                "Через окно ползёт пятно солнца — грейся в нём и копи очки, выталкивай соперников. Облака прячут солнце, хозяйка брызгает из пульверизатора в лидера.",
                "SunSpot", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);

            // «Сумо» → «Солнечное пятно» на том же месте каталога.
            var old = AssetDatabase.LoadAssetAtPath<MiniGameDefinition>(OldDefinition);
            if (old != null) catalog.EditorReplace(old, definition);
            else catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);
            RemoveSceneFromBuild(OldScene);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 38 готов: «Солнечное пятно» (в каталоге №{index + 1}).");
        }

        private static void RemoveSceneFromBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 5.5f, -8.5f), Quaternion.Euler(30f, 0f, 0f));

            var room = ArtKit.Group("LivingRoom");
            var glass = BuildRoom(room);
            BuildFurniture(room);
            BuildOutside();
            var (hostess, nozzle) = BuildHostess();
            var visual = BuildSunVisual();

            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -3.6f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 1.4f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("SunSpotMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<SunSpotMode>();
            var so = new SerializedObject(mode);
            so.FindProperty("miniGameId").stringValue = "sun_spot";
            so.FindProperty("timeLimitSeconds").floatValue = 75f;
            so.FindProperty("endWhenOnePlayerLeft").boolValue = false;
            so.FindProperty("hostessNozzle").objectReferenceValue = nozzle;
            so.FindProperty("windowY").floatValue = (SillTop + WindowTop) * 0.5f + 0.1f;
            so.FindProperty("windowZ").floatValue = HalfZ + 0.4f;
            so.FindProperty("windowHalfX").floatValue = WindowHalf - 1f;
            // Маршрут пятна: подоконник → лежанка → кресло → пол → столик → пол → когтеточка → диван → пуф.
            Vector3[] path =
            {
                new(-2.2f, SillTop, 4.6f),
                new(2.2f, SillTop, 4.6f),
                new(3.2f, 0f, 2.2f),
                new(4.4f, 0.45f, 1.2f),
                new(3.0f, 0f, -1.2f),
                new(0.2f, 0.45f, -0.4f),
                new(-1.8f, 0f, -2.4f),
                new(-5.2f, 1.1f, -2.2f),
                new(-3.4f, 0.45f, 1.3f),
                new(-1.0f, 0.4f, 2.8f),
            };
            var pathProperty = so.FindProperty("path");
            pathProperty.arraySize = path.Length;
            for (int i = 0; i < path.Length; i++) pathProperty.GetArrayElementAtIndex(i).vector3Value = path[i];
            var ignore = so.FindProperty("ignoreColliders");
            ignore.arraySize = 1;
            ignore.GetArrayElementAtIndex(0).objectReferenceValue = glass;
            so.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 30f, 120f);
            // Солнце светит из окна (с севера), без теней от стен и потолка — комната ровно освещена,
            // а «настоящее» солнце рисует пятно.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.transform.rotation = Quaternion.Euler(32f, 160f, 0f);
                l.intensity = 0.55f;
            }
            // Комната в полутени — солнечный свет из окна на её фоне яркий.
            RenderSettings.ambientSkyColor *= 0.7f;
            RenderSettings.ambientEquatorColor *= 0.7f;
            RenderSettings.ambientGroundColor *= 0.75f;
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        // ───────────────────────── Комната ─────────────────────────

        private static Collider BuildRoom(Transform room)
        {
            var floor = ArtLibrary.Parquet;
            var wallpaper = ArtLibrary.Wallpaper;
            var trim = ArtKit.Color("Skirting", new Color(0.93f, 0.89f, 0.80f));
            float t = 0.3f;

            ArtKit.UvBox("Floor", new Vector3(0f, -0.3f, 0f), new Vector3(HalfX * 2f, 0.3f, HalfZ * 2f), floor, floor, new Vector2(1.5f, 1.5f), 1.5f, room);
            NoShadow(ArtKit.UvBox("WallSouth", new Vector3(0f, 0f, -HalfZ - t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallWest", new Vector3(-HalfX - t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallEast", new Vector3(HalfX + t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, trim, new Vector2(1f, 1f), 1f, room));

            // Северная стена с большим окном: низ, верх, простенки.
            float nz = HalfZ + t * 0.5f;
            float sideWidth = HalfX - WindowHalf + t;
            NoShadow(ArtKit.UvBox("WallNorthLow", new Vector3(0f, 0f, nz), new Vector3(HalfX * 2f + t * 2f, SillTop, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallNorthTop", new Vector3(0f, WindowTop, nz), new Vector3(HalfX * 2f + t * 2f, WallHeight - WindowTop, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallNorthLeft", new Vector3(-(WindowHalf + sideWidth * 0.5f), SillTop, nz), new Vector3(sideWidth, WindowTop - SillTop, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallNorthRight", new Vector3(WindowHalf + sideWidth * 0.5f, SillTop, nz), new Vector3(sideWidth, WindowTop - SillTop, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room));

            // Широкий подоконник (на него запрыгивают) и рама.
            var sillWhite = ArtKit.Color("SillWhite", new Color(0.96f, 0.95f, 0.92f));
            ArtKit.UvBox("Sill", new Vector3(0f, SillTop - 0.08f, HalfZ - 0.35f), new Vector3(WindowHalf * 2f + 0.4f, 0.08f, 0.9f), sillWhite, sillWhite, new Vector2(1f, 1f), 1f, room);
            ArtKit.Box("SillBase", new Vector3(0f, (SillTop - 0.08f) * 0.5f, HalfZ - 0.05f), new Vector3(WindowHalf * 2f + 0.2f, SillTop - 0.08f, 0.1f), sillWhite, room, false);
            var frame = ArtKit.Color("WindowFrame", new Color(0.96f, 0.95f, 0.92f));
            float fz = HalfZ + t;
            ArtKit.Box("FrameTop", new Vector3(0f, WindowTop - 0.04f, fz), new Vector3(WindowHalf * 2f, 0.08f, 0.08f), frame, room, false);
            ArtKit.Box("FrameLeft", new Vector3(-WindowHalf + 0.04f, (SillTop + WindowTop) * 0.5f, fz), new Vector3(0.08f, WindowTop - SillTop, 0.08f), frame, room, false);
            ArtKit.Box("FrameRight", new Vector3(WindowHalf - 0.04f, (SillTop + WindowTop) * 0.5f, fz), new Vector3(0.08f, WindowTop - SillTop, 0.08f), frame, room, false);

            // Шторы раздвинуты по краям, карниз.
            var curtain = ArtKit.Color("CurtainLiving", new Color(0.93f, 0.66f, 0.44f));
            foreach (float side in new[] { -1f, 1f })
                ArtKit.Box("Curtain", new Vector3(side * (WindowHalf + 0.25f), (SillTop + WindowTop) * 0.5f + 0.15f, HalfZ - 0.1f), new Vector3(0.7f, WindowTop - SillTop + 0.5f, 0.06f), curtain, room, false);
            ArtKit.Box("CurtainRod", new Vector3(0f, WindowTop + 0.12f, HalfZ - 0.1f), new Vector3(WindowHalf * 2f + 1.6f, 0.04f, 0.04f), ArtLibrary.MetalDark, room, false);

            // Невидимое стекло: коты не выпрыгивают в окно, свет проходит (SunSpotMode игнорирует его).
            var glassGo = new GameObject("WindowGlass");
            glassGo.transform.SetParent(room, false);
            glassGo.transform.position = new Vector3(0f, (SillTop + WindowTop) * 0.5f, HalfZ + 0.05f);
            var glass = glassGo.AddComponent<BoxCollider>();
            glass.size = new Vector3(WindowHalf * 2f, WindowTop - SillTop, 0.1f);

            // Потолок (без теней), люстра.
            var plaster = ArtKit.Color("CeilingPlaster", new Color(0.96f, 0.94f, 0.89f));
            var ceiling = ArtKit.UvBox("Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(HalfX * 2f + t * 2f, 0.25f, HalfZ * 2f + t * 2f), plaster, plaster, new Vector2(2f, 2f), 2f, room);
            NoShadow(ceiling);
            MaoshiSetupStage22.Chandelier(room, new Vector3(0f, WallHeight, 0.4f), 3f, 0.1f);
            // Днём люстра выключена: в этой сцене доп. источники светят в мультяшном шейдере,
            // и её свет давал яркое круглое пятно на потолке над окном.
            var chandelierLight = room.Find("ChandelierLight");
            if (chandelierLight != null && chandelierLight.TryGetComponent<Light>(out var cl)) cl.enabled = false;

            // Дверь на восточной стене — в ней стоит хозяйка.
            var doorWood = ArtKit.Color("DoorWood", new Color(0.55f, 0.36f, 0.22f));
            ArtKit.Box("DoorFrame", new Vector3(HalfX - 0.02f, 1.1f, -3.2f), new Vector3(0.06f, 2.25f, 1.25f), trim, room, false);
            ArtKit.Box("DoorDark", new Vector3(HalfX - 0.01f, 1.05f, -3.2f), new Vector3(0.05f, 2.1f, 1.05f), ArtKit.Color("Hallway", new Color(0.30f, 0.24f, 0.22f)), room, false);
            ArtKit.Box("DoorLeaf", new Vector3(HalfX - 0.45f, 1.05f, -3.78f), new Vector3(0.9f, 2.1f, 0.05f), doorWood, room, false, 0f);
            return glass;
        }

        private static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ───────────────────────── Мебель ─────────────────────────

        private static void BuildFurniture(Transform room)
        {
            var fabric = ArtKit.Color("SofaFabric", new Color(0.36f, 0.52f, 0.62f));
            var fabricDark = ArtKit.Color("SofaFabricDark", new Color(0.28f, 0.40f, 0.50f));
            var wood = ArtLibrary.Wood;
            var woodDark = ArtLibrary.WoodDark;

            // Диван спинкой к окну. Коллайдеры: сиденье 0.45 и спинка — чтобы пятно ложилось на сиденье.
            Vector3 sofa = new Vector3(-3.4f, 0f, 1.6f);
            var sofaModel = ArtKit.PlaceFit("Furniture/loungeSofa", sofa, 180f, new Vector3(2.6f, 0.95f, 1.0f), room);
            if (sofaModel == null)
            {
                ArtKit.Box("SofaSeatVisual", sofa + new Vector3(0f, 0.22f, -0.1f), new Vector3(2.6f, 0.45f, 0.8f), fabric, room, false);
                ArtKit.Box("SofaBackVisual", sofa + new Vector3(0f, 0.47f, 0.38f), new Vector3(2.6f, 0.95f, 0.24f), fabricDark, room, false);
            }
            ArtKit.Box("SofaSeat", sofa + new Vector3(0f, 0.225f, -0.06f), new Vector3(2.5f, 0.45f, 0.88f), fabric, room).GetComponent<Renderer>().enabled = false;
            ArtKit.Box("SofaBack", sofa + new Vector3(0f, 0.47f, 0.4f), new Vector3(2.6f, 0.95f, 0.2f), fabric, room).GetComponent<Renderer>().enabled = false;

            // Кресло (из коробок) у восточной стены, лицом в комнату.
            Vector3 chair = new Vector3(4.4f, 0f, 1.2f);
            ArtKit.Box("ArmchairSeat", chair + new Vector3(0f, 0.225f, 0f), new Vector3(0.95f, 0.45f, 0.9f), fabric, room);
            ArtKit.Box("ArmchairCushion", chair + new Vector3(-0.02f, 0.47f, 0f), new Vector3(0.8f, 0.06f, 0.75f), ArtKit.Color("Cushion", new Color(0.92f, 0.78f, 0.52f)), room, false);
            ArtKit.Box("ArmchairBack", chair + new Vector3(0.42f, 0.5f, 0f), new Vector3(0.18f, 1.0f, 0.9f), fabricDark, room);
            foreach (float side in new[] { -1f, 1f })
                ArtKit.Box("ArmchairArm", chair + new Vector3(0f, 0.33f, side * 0.48f), new Vector3(0.95f, 0.66f, 0.16f), fabricDark, room);

            // Пуф у окна.
            ArtKit.Cylinder("Pouf", new Vector3(-1.0f, 0f, 2.8f), 0.36f, 0.4f, ArtKit.Color("Pouf", new Color(0.82f, 0.42f, 0.38f)), room);

            // Журнальный столик: коллайдер только у столешницы — под ним тень, можно спрятаться.
            Vector3 table = new Vector3(0.2f, 0f, -0.4f);
            ArtKit.PlaceFit("Furniture/rugRound", new Vector3(0.2f, 0.005f, -0.4f), 0f, new Vector3(3.4f, 0.02f, 3.4f), room);
            ArtKit.Box("TableTop", table + new Vector3(0f, 0.42f, 0f), new Vector3(1.3f, 0.06f, 0.75f), wood, room);
            foreach (var leg in new[] { new Vector3(-0.58f, 0f, -0.3f), new Vector3(0.58f, 0f, -0.3f), new Vector3(-0.58f, 0f, 0.3f), new Vector3(0.58f, 0f, 0.3f) })
                ArtKit.Box("TableLeg", table + leg + new Vector3(0f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.06f), woodDark, room, false);
            ArtKit.Cylinder("Mug", table + new Vector3(0.3f, 0.45f, 0.1f), 0.06f, 0.1f, ArtKit.Color("MugRed", new Color(0.82f, 0.28f, 0.24f)), room, false);
            ArtKit.Box("Book", table + new Vector3(-0.25f, 0.47f, -0.05f), new Vector3(0.3f, 0.04f, 0.22f), ArtKit.Color("BookBlue", new Color(0.30f, 0.42f, 0.70f)), room, false, 15f);

            // Кошачий домик-когтеточка: две площадки (0.6 и 1.1 м), до верхней — с нижней.
            Vector3 tree = new Vector3(-5.2f, 0f, -2.2f);
            var carpet = ArtKit.Color("CatTreeCarpet", new Color(0.86f, 0.80f, 0.70f));
            var rope = ArtKit.Color("SisalRope", new Color(0.78f, 0.66f, 0.46f));
            ArtKit.Box("TreeBase", tree + new Vector3(0f, 0.04f, 0f), new Vector3(1.1f, 0.08f, 1.1f), carpet, room);
            ArtKit.Cylinder("TreePost", tree, 0.1f, 1.1f, rope, room);
            ArtKit.Cylinder("TreePost2", tree + new Vector3(0.75f, 0f, 0.55f), 0.09f, 0.6f, rope, room);
            ArtKit.Box("TreeLow", tree + new Vector3(0.75f, 0.6f, 0.55f), new Vector3(0.6f, 0.06f, 0.6f), carpet, room);
            ArtKit.Box("TreeTop", tree + new Vector3(0f, 1.1f, 0f), new Vector3(0.75f, 0.07f, 0.75f), carpet, room);
            ArtKit.Box("TreeTopRim", tree + new Vector3(0f, 1.16f, 0.36f), new Vector3(0.75f, 0.06f, 0.04f), carpet, room, false);

            // Лежанка у окна (без коллайдера) и миска.
            var bedMesh = MaoshiSetupStage22.LatheMesh("CatBed", new[]
            {
                new Vector2(0.45f, 0f), new Vector2(0.5f, 0.1f), new Vector2(0.46f, 0.2f), new Vector2(0.38f, 0.18f),
                new Vector2(0.34f, 0.08f), new Vector2(0.001f, 0.07f),
            });
            var bed = new GameObject("CatBed");
            bed.transform.SetParent(room, false);
            bed.transform.position = new Vector3(3.2f, 0f, 2.2f);
            bed.AddComponent<MeshFilter>().sharedMesh = bedMesh;
            bed.AddComponent<MeshRenderer>().sharedMaterial = ArtKit.Color("CatBedFabric", new Color(0.62f, 0.44f, 0.70f));

            // Вдоль стен: книжный шкаф, тумба с телевизором, торшер, цветы на подоконнике.
            ArtKit.PlaceFit("Furniture/bookcaseOpen", new Vector3(HalfX - 0.3f, 0f, 3.2f), -90f, new Vector3(0.5f, 1.9f, 1.2f), room, true);
            ArtKit.PlaceFit("Furniture/cabinetTelevision", new Vector3(-1.5f, 0f, -HalfZ + 0.3f), 0f, new Vector3(1.6f, 0.6f, 0.5f), room, true);
            ArtKit.PlaceFit("Furniture/televisionVintage", new Vector3(-1.5f, 0.6f, -HalfZ + 0.3f), 0f, new Vector3(0.8f, 0.6f, 0.55f), room);
            ArtKit.PlaceFit("Furniture/lampSquareFloor", new Vector3(-HalfX + 0.5f, 0f, 3.8f), 0f, new Vector3(0.4f, 1.7f, 0.4f), room, true);
            ArtKit.PlaceFit("Furniture/pottedPlant", new Vector3(5.6f, 0f, -0.8f), 0f, new Vector3(0.6f, 1.2f, 0.6f), room, true);
            ArtKit.PlaceFit("Furniture/plantSmall1", new Vector3(-WindowHalf + 0.25f, SillTop, HalfZ - 0.3f), 0f, new Vector3(0.35f, 0.45f, 0.35f), room);
            ArtKit.PlaceFit("Furniture/plantSmall3", new Vector3(WindowHalf - 0.25f, SillTop, HalfZ - 0.3f), 0f, new Vector3(0.35f, 0.45f, 0.35f), room);
            // Картины.
            ArtKit.Box("Picture", new Vector3(-HalfX + 0.02f, 1.8f, -0.5f), new Vector3(0.04f, 0.7f, 1.0f), ArtKit.Color("PictureSea", new Color(0.42f, 0.62f, 0.80f)), room, false);
            ArtKit.Box("PictureFrame", new Vector3(-HalfX + 0.01f, 1.8f, -0.5f), new Vector3(0.03f, 0.8f, 1.1f), woodDark, room, false);
        }

        /// <summary>За окном: двор внизу (мы на втором этаже) и дома напротив.</summary>
        private static void BuildOutside()
        {
            var root = ArtKit.Group("Outside");
            ArtKit.UvBox("YardGround", new Vector3(0f, -6.5f, 20f), new Vector3(60f, 0.2f, 30f), ArtLibrary.Asphalt, ArtLibrary.Grass, new Vector2(4f, 4f), 6f, root, false);
            ArtLibrary.House(root, new Vector3(-12f, -6.4f, 30f), new Vector3(20f, ArtLibrary.Floor * 5f, 10f), ArtLibrary.FacadePanel);
            ArtLibrary.House(root, new Vector3(12f, -6.4f, 34f), new Vector3(18f, ArtLibrary.Floor * 6f, 10f), ArtLibrary.FacadeBrick);
            ArtLibrary.Tree(root, "Nature/tree_oak", new Vector3(-11f, -6.4f, 18f), 8f, false);
            ArtLibrary.Tree(root, "Nature/tree_default", new Vector3(11f, -6.4f, 21f), 8f, false);
        }

        // ───────────────────────── Хозяйка ─────────────────────────

        private static (SunHostess hostess, Transform nozzle) BuildHostess()
        {
            var root = new GameObject("Hostess");
            root.transform.SetPositionAndRotation(new Vector3(HalfX - 0.55f, 0f, -3.2f), Quaternion.Euler(0f, -90f, 0f));
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.32f;

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            AddHostessModel(body);

            // Пульверизатор в правой руке: поднимается к цели (поворот вокруг плеча).
            var pivot = new GameObject("BottlePivot").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.3f, 1.3f, 0.05f);
            var plastic = ArtKit.Color("SprayBottle", new Color(0.35f, 0.75f, 0.92f));
            var bottle = ArtKit.Cylinder("Bottle", Vector3.zero, 0.07f, 0.24f, plastic, pivot, false);
            bottle.isStatic = false;
            bottle.transform.localPosition = new Vector3(0f, -0.35f, 0.1f);
            var head = ArtKit.Box("SprayHead", Vector3.zero, new Vector3(0.08f, 0.08f, 0.14f), ArtKit.Color("SprayHead", new Color(0.95f, 0.95f, 0.95f)), pivot, false);
            head.isStatic = false;
            head.transform.localPosition = new Vector3(0f, -0.17f, 0.14f);
            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(pivot, false);
            nozzle.localPosition = new Vector3(0f, -0.17f, 0.22f);

            // Знак «!» над головой.
            var signGo = new GameObject("Sign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var sign = signGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.font = font;
            sign.fontSize = 96;
            sign.characterSize = 0.07f;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.fontStyle = FontStyle.Bold;
            sign.color = new Color(0.2f, 0.45f, 0.9f);
            sign.text = "";
            signGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            // Круг-предупреждение (синее кольцо).
            var ring = new GameObject("SprayRing");
            ring.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            var ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = AdditiveMaterial("SprayRing", new Color(0.35f, 0.7f, 1f, 0.9f));
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            var hostess = root.AddComponent<SunHostess>();
            var so = new SerializedObject(hostess);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("bottlePivot").objectReferenceValue = pivot;
            so.FindProperty("nozzle").objectReferenceValue = nozzle;
            so.FindProperty("sign").objectReferenceValue = sign;
            so.FindProperty("ring").objectReferenceValue = ring.transform;
            so.FindProperty("water").objectReferenceValue = ArtKit.Color("WaterDrop", new Color(0.45f, 0.75f, 0.98f), 0.8f);
            so.FindProperty("dropMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            so.ApplyModifiedPropertiesWithoutUndo();
            return (hostess, nozzle);
        }

        /// <summary>Хозяйка/бабушка: персонаж Kenney 1.7 м со своим скином и idle-анимацией (или простая фигура без моделей).</summary>
        internal static void AddHostessModel(Transform body)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyImportSettings.CharacterModel);
            if (model != null)
            {
                var character = (GameObject)PrefabUtility.InstantiatePrefab(model);
                character.name = "Character";
                character.transform.SetParent(body, false);
                var natural = ArtKit.RendererBounds(character);
                character.transform.localScale = Vector3.one * (1.7f / Mathf.Max(0.01f, natural.size.y));
                var placed = ArtKit.RendererBounds(character);
                character.transform.position += Vector3.up * (0f - placed.min.y);
                var skin = ArtKit.Textured("HostessSkin", ArtKit.ArtFolder + "/Textures/HostessSkin.png");
                foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = skin;
                    r.sharedMaterials = materials;
                }
                var animator = character.GetComponent<Animator>();
                if (animator == null) animator = character.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HostessController);
                animator.applyRootMotion = false;
            }
            else
            {
                var dress = ArtKit.Color("HostessDress", new Color(0.36f, 0.50f, 0.78f));
                ArtKit.Cylinder("Dress", Vector3.zero, 0.3f, 1.2f, dress, body, false).transform.localPosition = new Vector3(0f, 0.6f, 0f);
            }

        }

        // ───────────────────────── Пятно солнца ─────────────────────────

        private static SunPatchVisual BuildSunVisual()
        {
            var go = new GameObject("SunVisual");
            // Лучи в воздухе: призма по форме окна с текстурой полос (сложение).
            var shaft = new GameObject("Shaft");
            shaft.transform.SetParent(go.transform, false);
            var shaftFilter = shaft.AddComponent<MeshFilter>();
            var shaftRenderer = shaft.AddComponent<MeshRenderer>();
            var shaftMaterial = AdditiveMaterial("SunShaft", Color.white, cullOff: true);
            shaftMaterial.SetTexture("_BaseMap", RaysTexture());
            EditorUtility.SetDirty(shaftMaterial);
            shaftRenderer.sharedMaterial = shaftMaterial;
            shaftRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shaftRenderer.receiveShadows = false;

            // Настоящий свет солнца: прожектор далеко за окном с маской-окном и мягкими тенями
            // (двигает и наводит SunPatchVisual).
            var lightGo = new GameObject("SunLight");
            lightGo.transform.SetParent(go.transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Spot;
            sun.color = new Color(1f, 0.86f, 0.6f);
            sun.intensity = 400f;
            sun.range = 25f;
            sun.spotAngle = 10f;
            sun.innerSpotAngle = 9.8f;
            sun.cookie = CookieTexture();
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.shadowNearPlane = 0.5f;
            sun.renderMode = LightRenderMode.ForcePixel;
            var lightData = lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            var lightSo = new SerializedObject(lightData);
            var tier = lightSo.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (tier != null) tier.intValue = UnityEngine.Rendering.Universal.UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            lightSo.ApplyModifiedPropertiesWithoutUndo();

            var visual = go.AddComponent<SunPatchVisual>();
            var so = new SerializedObject(visual);
            so.FindProperty("shaft").objectReferenceValue = shaftFilter;
            so.FindProperty("sunLight").objectReferenceValue = sun;
            so.FindProperty("cookieWidth").floatValue = CookieWidth;
            so.FindProperty("dustMaterial").objectReferenceValue = AdditiveMaterial("SunDust", Color.white);
            so.FindProperty("dustMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            so.ApplyModifiedPropertiesWithoutUndo();
            return visual;
        }

        // Маска окна в cookie: ширина и высота светлого прямоугольника в долях текстуры (как SunSpotMode: 1.15 : 0.8).
        private const float CookieWidth = 0.9f;
        private const float CookieHeight = CookieWidth * SunSpotMode.HalfHeightK / SunSpotMode.HalfWidthK;

        /// <summary>Маска света «окно»: светлый прямоугольник с мягкими краями и лёгкой неровностью (тюль).</summary>
        private static Texture2D CookieTexture()
        {
            const string path = ArtKit.ArtFolder + "/Textures/SunCookie.png";
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(38);
            float[] lace = new float[size];
            for (int i = 0; i < size; i++) lace[i] = 0.9f + 0.1f * (float)rng.NextDouble();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float ex = Mathf.Clamp01((CookieWidth - Mathf.Abs(u)) / 0.06f);
                float ey = Mathf.Clamp01((CookieHeight - Mathf.Abs(v)) / 0.06f);
                float k = Mathf.SmoothStep(0f, 1f, ex) * Mathf.SmoothStep(0f, 1f, ey);
                // Еле заметные полосы от тюля — свет «живой», а не плоский.
                float stripes = 0.9f + 0.1f * Mathf.Sin(u * 38f + Mathf.Sin(v * 7f) * 1.5f) * lace[x];
                // Рама окна: крестовина (4 стекла) — тень переплёта на полу, сразу видно «это свет из окна».
                float bar = 0.035f;
                float mullion = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(u) - bar) / 0.02f));
                float transom = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(v - CookieHeight * 0.25f) - bar * 0.8f) / 0.02f));
                float c = k * stripes * mullion * transom;
                tex.SetPixel(x, y, new Color(c, c, c, c));
            }
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Текстура лучей: мягкие продольные полосы разной яркости (по u), повторяется.</summary>
        private static Texture2D RaysTexture()
        {
            const string path = ArtKit.ArtFolder + "/Textures/SunRays.png";
            const int w = 256, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var rng = new System.Random(7);
            float[] column = new float[w];
            for (int ray = 0; ray < 14; ray++)
            {
                float center = (float)rng.NextDouble() * w, width = 3f + (float)rng.NextDouble() * 14f, power = 0.35f + 0.65f * (float)rng.NextDouble();
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x - center);
                    d = Mathf.Min(d, w - d); // повторяется без шва
                    column[x] += power * Mathf.Exp(-d * d / (width * width));
                }
            }
            for (int x = 0; x < w; x++) column[x] = Mathf.Clamp01(0.25f + column[x] * 0.8f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, column[x]));
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Прибавляющийся (светящийся) материал с цветом вершин — URP Particles/Unlit, Additive.</summary>
        internal static Material AdditiveMaterial(string name, Color color, bool cullOff = true, bool additive = true)
        {
            string path = $"{ArtKit.MaterialsFolder}/M_{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_Cull", cullOff ? 0f : 2f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Плоское кольцо радиусом ~1.1 м (пол), с мягкой серединой.</summary>
        internal static Mesh RingMesh()
        {
            string path = MaoshiSetupStage22.MeshFolder + "/SprayRing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = "SprayRing" };
            const int segments = 40;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            float[] radii = { 0.0f, 0.85f, 1.0f, 1.1f };
            float[] alpha = { 0.18f, 0.25f, 1f, 0f };
            for (int ring = 0; ring < radii.Length; ring++)
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * radii[ring], 0f, Mathf.Sin(a) * radii[ring]));
                colors.Add(new Color(1f, 1f, 1f, alpha[ring]));
            }
            int row = segments + 1;
            for (int ring = 0; ring < radii.Length - 1; ring++)
            for (int i = 0; i < segments; i++)
            {
                int a = ring * row + i, b = a + 1, c = a + row, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
