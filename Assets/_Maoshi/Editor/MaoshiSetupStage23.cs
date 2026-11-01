using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.SausageKitchen;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 23: мини-игра «Колбаса с кухни».
    /// Кухня 16×12 м с потолком: плиточный пол, кафель у плиты, кухонный гарнитур, холодильник, окно, дверь.
    /// Хозяйка (персонаж Kenney) у плиты, за её спиной стол с тарелкой колбасы. Посреди кухни укрытия:
    /// коробки, стиральная машина, шкаф. Миски котов — у дальней стены (8 штук, расставляются по числу игроков).
    /// Запуск: Maoshi → Setup → Stage 23 - Sausage Kitchen. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage23
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/SausageKitchen";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/SausageKitchen.unity";
        private const string HostessController = ArtKit.ArtFolder + "/Animators/AC_Hostess.controller";

        private const float HalfX = 8f;
        private const float HalfZ = 6f;
        private const float WallHeight = 3.1f;
        private const float HostessZ = 4.75f;

        [MenuItem("Maoshi/Setup/Stage 23 - Sausage Kitchen")]
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
            ConfigureTextures();

            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/SausageKitchen.asset");
            definition.EditorSetup("sausage_kitchen", "Колбаса с кухни",
                "Бабушка жарит котлеты спиной к столу с колбасой. Хватай кусок и неси в свою миску! Обернулась — замри, иначе получишь полотенцем.",
                "SausageKitchen", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 23 готов: «Колбаса с кухни» (в каталоге №{index + 1}; во дворе в редакторе — клавиша {(index < 9 ? (index + 1).ToString() : "0")}).");
        }

        private static void ConfigureTextures()
        {
            foreach (var name in new[] { "KitchenTiles", "WallTiles", "HostessSkin" })
            {
                string path = $"{ArtKit.ArtFolder}/Textures/{name}.png";
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 6f, -9f), Quaternion.Euler(30f, 0f, 0f));

            var room = ArtKit.Group("Kitchen");
            BuildRoom(room);
            BuildKitchenLine(room);
            var hostess = BuildHostess();
            var (piece, stick) = BuildTemplates();
            var plate = BuildTable(room, stick);
            BuildCover(room);
            var bowls = BuildBowls();

            // Коты в ряд перед своими мисками, мордой к хозяйке.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4.4f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 1.5f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("SausageMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<SausageMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "sausage_kitchen";
            serialized.FindProperty("timeLimitSeconds").floatValue = 75f;
            serialized.FindProperty("hostess").objectReferenceValue = hostess;
            serialized.FindProperty("plate").objectReferenceValue = plate;
            serialized.FindProperty("pieceTemplate").objectReferenceValue = piece;
            serialized.FindProperty("stickTemplate").objectReferenceValue = stick;
            serialized.FindProperty("bowlZ").floatValue = -5.2f;
            serialized.FindProperty("bowlSpacing").floatValue = 1.5f;
            var bowlsProperty = serialized.FindProperty("bowls");
            bowlsProperty.arraySize = bowls.Length;
            for (int i = 0; i < bowls.Length; i++) bowlsProperty.GetArrayElementAtIndex(i).objectReferenceValue = bowls[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 30f, 110f);
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        // ───────────────────────── Комната ─────────────────────────

        private static void BuildRoom(Transform room)
        {
            var floor = ArtLibrary.Tex("KitchenTiles");
            var wallTiles = ArtLibrary.Tex("WallTiles");
            var wallpaper = ArtKit.Color("KitchenPaint", new Color(0.93f, 0.86f, 0.62f));
            var trim = ArtKit.Color("Skirting", new Color(0.93f, 0.89f, 0.80f));
            float t = 0.3f;

            ArtKit.UvBox("Floor", new Vector3(0f, -0.3f, 0f), new Vector3(HalfX * 2f, 0.3f, HalfZ * 2f), floor, floor, new Vector2(0.8f, 0.8f), 0.8f, room);
            ArtKit.UvBox("WallNorth", new Vector3(0f, 0f, HalfZ + t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room);
            ArtKit.UvBox("WallSouth", new Vector3(0f, 0f, -HalfZ - t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wallpaper, trim, new Vector2(1f, 1f), 1f, room);
            ArtKit.UvBox("WallWest", new Vector3(-HalfX - t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, trim, new Vector2(1f, 1f), 1f, room);
            ArtKit.UvBox("WallEast", new Vector3(HalfX + t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wallpaper, trim, new Vector2(1f, 1f), 1f, room);

            // Кафель: по всей стене с плитой и полосой понизу на остальных.
            ArtKit.UvBox("TilesNorth", new Vector3(0f, 0f, HalfZ - 0.01f), new Vector3(HalfX * 2f, 1.7f, 0.02f), wallTiles, trim, new Vector2(0.9f, 0.9f), 1f, room, false);
            ArtKit.UvBox("TilesWest", new Vector3(-HalfX + 0.01f, 0f, 0f), new Vector3(0.02f, 1.0f, HalfZ * 2f), wallTiles, trim, new Vector2(0.9f, 0.9f), 1f, room, false);
            ArtKit.UvBox("TilesEast", new Vector3(HalfX - 0.01f, 0f, 0f), new Vector3(0.02f, 1.0f, HalfZ * 2f), wallTiles, trim, new Vector2(0.9f, 0.9f), 1f, room, false);
            ArtKit.UvBox("TilesSouth", new Vector3(0f, 0f, -HalfZ + 0.01f), new Vector3(HalfX * 2f, 1.0f, 0.02f), wallTiles, trim, new Vector2(0.9f, 0.9f), 1f, room, false);

            // Потолок и свет.
            var plaster = ArtKit.Color("CeilingPlaster", new Color(0.96f, 0.94f, 0.89f));
            var ceiling = ArtKit.UvBox("Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(HalfX * 2f + t * 2f, 0.25f, HalfZ * 2f + t * 2f), plaster, plaster, new Vector2(2f, 2f), 2f, room);
            ceiling.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Плафоны под самым потолком — не загораживают вид сверху.
            MaoshiSetupStage22.Chandelier(room, new Vector3(-2.5f, WallHeight, 1.6f), 3f, 0.08f);
            MaoshiSetupStage22.Chandelier(room, new Vector3(2.5f, WallHeight, -2.6f), 3f, 0.08f);

            // Окно на левой стене.
            var sky = ArtKit.Color("WindowSky", new Color(0.62f, 0.78f, 0.96f));
            var frame = ArtKit.Color("WindowFrame", new Color(0.96f, 0.95f, 0.92f));
            var curtain = ArtKit.Color("CurtainKitchen", new Color(0.95f, 0.55f, 0.40f));
            float wx = -HalfX + 0.03f;
            ArtKit.Box("WindowGlass", new Vector3(wx, 1.8f, 0.5f), new Vector3(0.02f, 1.3f, 1.8f), sky, room, false);
            ArtKit.Box("FrameTop", new Vector3(wx + 0.02f, 2.48f, 0.5f), new Vector3(0.06f, 0.08f, 2.0f), frame, room, false);
            ArtKit.Box("FrameBottom", new Vector3(wx + 0.02f, 1.12f, 0.5f), new Vector3(0.06f, 0.08f, 2.0f), frame, room, false);
            ArtKit.Box("FrameMid", new Vector3(wx + 0.02f, 1.8f, 0.5f), new Vector3(0.06f, 1.3f, 0.06f), frame, room, false);
            ArtKit.Box("CafeCurtain", new Vector3(wx + 0.06f, 1.45f, 0.5f), new Vector3(0.03f, 0.6f, 1.9f), curtain, room, false);
            ArtKit.PlaceFit("Furniture/plantSmall2", new Vector3(wx + 0.25f, 0f, -1.2f), 0f, new Vector3(0.5f, 0.9f, 0.5f), room, true);

            // Дверь на ближней стене, часы над ней.
            var doorWood = ArtKit.Color("DoorWood", new Color(0.55f, 0.36f, 0.22f));
            float dz = -HalfZ + 0.03f;
            ArtKit.Box("Door", new Vector3(5.6f, 1.05f, dz + 0.02f), new Vector3(1.0f, 2.1f, 0.05f), doorWood, room, false);
            ArtKit.Box("DoorFrame", new Vector3(5.6f, 2.15f, dz + 0.04f), new Vector3(1.2f, 0.1f, 0.06f), trim, room, false);
            ArtKit.Cylinder("Clock", new Vector3(0f, 2.5f, dz + 0.02f), 0.28f, 0.04f, frame, room, false).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ───────────────────────── Кухонный гарнитур ─────────────────────────

        private static void BuildKitchenLine(Transform room)
        {
            float z = HalfZ - 0.4f;
            ArtKit.PlaceFit("Furniture/kitchenStove", new Vector3(0f, 0f, z), 180f, new Vector3(0.9f, 0.95f, 0.75f), room, true);
            for (int i = 1; i <= 3; i++)
            {
                ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(-i * 0.95f, 0f, z), 180f, new Vector3(0.95f, 0.95f, 0.75f), room, true);
                ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(i * 0.95f, 0f, z), 180f, new Vector3(0.95f, 0.95f, 0.75f), room, true);
            }
            ArtKit.PlaceFit("Furniture/kitchenFridge", new Vector3(-4.0f, 0f, z), 180f, new Vector3(0.95f, 2.0f, 0.8f), room, true);
            ArtKit.PlaceFit("Furniture/washer", new Vector3(4.0f, 0f, z), 180f, new Vector3(0.9f, 0.95f, 0.75f), room, true);

            // Вытяжка и сковородка с котлетами.
            var metal = ArtLibrary.MetalDark;
            ArtKit.Box("Hood", new Vector3(0f, 2.2f, HalfZ - 0.35f), new Vector3(0.9f, 0.35f, 0.6f), ArtKit.Color("HoodWhite", new Color(0.9f, 0.9f, 0.88f)), room, false);
            ArtKit.Cylinder("Pan", new Vector3(-0.2f, 0.96f, z + 0.05f), 0.2f, 0.05f, metal, room, false);
            ArtKit.Cylinder("PanHandle", new Vector3(-0.2f, 0.98f, z - 0.2f), 0.02f, 0.02f, metal, room, false).transform.localScale = new Vector3(0.04f, 0.2f, 0.04f);
            var cutlet = ArtKit.Color("Cutlet", new Color(0.52f, 0.30f, 0.16f));
            for (int i = 0; i < 3; i++)
                ArtKit.Cylinder("Cutlet", new Vector3(-0.28f + i * 0.09f, 1.0f, z + 0.03f + (i % 2) * 0.06f), 0.05f, 0.025f, cutlet, room, false);
            ArtKit.Cylinder("Pot", new Vector3(0.22f, 0.96f, z + 0.1f), 0.16f, 0.22f, ArtKit.Color("PotRed", new Color(0.78f, 0.24f, 0.20f)), room, false);

            // Полка с банками над столешницей.
            ArtKit.Box("Shelf", new Vector3(-2.3f, 1.85f, HalfZ - 0.15f), new Vector3(1.8f, 0.05f, 0.28f), ArtLibrary.Wood, room, false);
            Color[] jars = { new(0.85f, 0.55f, 0.25f), new(0.55f, 0.75f, 0.45f), new(0.9f, 0.85f, 0.6f), new(0.75f, 0.35f, 0.35f) };
            for (int i = 0; i < 4; i++)
                ArtKit.Cylinder("Jar", new Vector3(-3.0f + i * 0.45f, 1.875f, HalfZ - 0.15f), 0.09f, 0.22f, ArtKit.Color($"Jar{i}", jars[i]), room, false);
            ArtKit.PlaceFit("Furniture/radio", new Vector3(2.3f, 0.95f, z), 180f, new Vector3(0.4f, 0.3f, 0.25f), room);
        }

        // ───────────────────────── Хозяйка ─────────────────────────

        private static Hostess BuildHostess()
        {
            var root = new GameObject("Hostess");
            root.transform.SetPositionAndRotation(new Vector3(0f, 0f, HostessZ), Quaternion.identity);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.35f;

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            Animator animator = null;
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

                // Свой скин: седые волосы, очки, синее платье в горошек (перекрашенный janitor.png).
                var skin = ArtKit.Textured("HostessSkin", ArtKit.ArtFolder + "/Textures/HostessSkin.png");
                foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = skin;
                    r.sharedMaterials = materials;
                }

                animator = character.GetComponent<Animator>();
                if (animator == null) animator = character.AddComponent<Animator>();
                animator.runtimeAnimatorController = GetHostessController();
                animator.applyRootMotion = false;
            }
            else
            {
                // Запасной вариант без моделей Kenney.
                var dress = ArtKit.Color("HostessDress", new Color(0.36f, 0.50f, 0.78f));
                Local(ArtKit.Cylinder("Dress", Vector3.zero, 0.3f, 1.2f, dress, body, false), new Vector3(0f, 0.6f, 0f));
                Local(ArtKit.Box("Head", Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), ArtKit.Color("HostessSkinTone", new Color(0.93f, 0.76f, 0.62f)), body, false), new Vector3(0f, 1.45f, 0f));
            }

            // Фартук, косынка и полотенце в правой руке.
            var apron = ArtKit.Color("Apron", new Color(0.96f, 0.94f, 0.88f));
            Local(ArtKit.Box("Apron", Vector3.zero, new Vector3(0.42f, 0.6f, 0.02f), apron, body, false), new Vector3(0f, 0.75f, 0.17f));
            var towelPivot = new GameObject("TowelPivot").transform;
            towelPivot.SetParent(body, false);
            towelPivot.localPosition = new Vector3(0.32f, 1.0f, -0.05f);
            var towel = ArtKit.Box("Towel", Vector3.zero, new Vector3(0.12f, 0.55f, 0.03f), ArtKit.Color("Towel", new Color(0.92f, 0.36f, 0.32f)), towelPivot, false);
            towel.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            towel.isStatic = false;

            // Знак над головой.
            var signGo = new GameObject("Sign");
            signGo.transform.SetParent(root.transform, false);
            signGo.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            var sign = signGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.font = font;
            sign.fontSize = 96;
            sign.characterSize = 0.06f;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.fontStyle = FontStyle.Bold;
            sign.text = "";
            signGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            var hostess = root.AddComponent<Hostess>();
            var so = new SerializedObject(hostess);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("towel").objectReferenceValue = towelPivot;
            so.FindProperty("sign").objectReferenceValue = sign;
            so.FindProperty("stoveOffset").floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hostess;
        }

        /// <summary>Контроллер хозяйки: один бесконечный idle (персонаж стоит у плиты).</summary>
        private static AnimatorController GetHostessController()
        {
            MaoshiSetup.EnsureFolder(ArtKit.ArtFolder + "/Animators");
            AssetDatabase.DeleteAsset(HostessController);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(HostessController);
            AnimationClip idle = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{KenneyImportSettings.Root}/Characters/Animations/idle.fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) idle = clip;
            var machine = controller.layers[0].stateMachine;
            var state = machine.AddState("Idle");
            state.motion = idle;
            machine.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void Local(GameObject go, Vector3 localPosition) => go.transform.localPosition = localPosition;

        // ───────────────────────── Колбаса ─────────────────────────

        private static (GameObject piece, GameObject stick) BuildTemplates()
        {
            var templates = new GameObject("SausageTemplates");
            templates.transform.position = new Vector3(0f, -20f, 0f);
            var piece = Sausage("SausagePiece", 0.34f, templates.transform);
            var stick = Sausage("SausageStick", 0.62f, templates.transform);
            return (piece, stick);
        }

        /// <summary>Колбаса по центру своего объекта (модель Kenney или цилиндр), длиной length, без коллайдеров и статики.</summary>
        private static GameObject Sausage(string name, float length, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var model = ArtKit.PlaceFit("Food/sausage", Vector3.zero, 0f, new Vector3(length, length, length), root.transform);
            if (model == null)
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(model.GetComponent<Collider>());
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                model.transform.localScale = new Vector3(length * 0.25f, length * 0.5f, length * 0.25f);
                model.GetComponent<Renderer>().sharedMaterial = ArtKit.Color("Sausage", new Color(0.78f, 0.36f, 0.34f));
            }
            var bounds = ArtKit.RendererBounds(model);
            model.transform.position += root.transform.position - bounds.center;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (name == "SausageStick")
            {
                // «Докторская» — розовая, чтобы отличалась от куска.
                var pink = ArtKit.Color("SausageDoctor", new Color(0.93f, 0.58f, 0.58f));
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = pink;
                    r.sharedMaterials = materials;
                }
            }
            return root;
        }

        private static SausagePlate BuildTable(Transform room, GameObject stickTemplate)
        {
            var center = new Vector3(0f, 0f, 2.6f);
            var table = ArtKit.PlaceFit("Furniture/table", center, 0f, new Vector3(1.9f, 0.78f, 1.0f), room);
            float top = 0.78f;
            if (table != null)
            {
                // Коллайдер только у столешницы — под столом можно пройти (и спрятаться).
                var b = ArtKit.RendererBounds(table);
                top = b.max.y;
                var slab = new GameObject("TableTop");
                slab.transform.SetParent(room, false);
                slab.transform.position = new Vector3(b.center.x, b.max.y - 0.05f, b.center.z);
                slab.AddComponent<BoxCollider>().size = new Vector3(b.size.x, 0.1f, b.size.z);
            }
            ArtKit.Box("Tablecloth", center + Vector3.up * (top + 0.005f), new Vector3(1.3f, 0.01f, 0.8f), ArtKit.Color("Tablecloth", new Color(0.95f, 0.92f, 0.86f)), room, false);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(-1.35f, 0f, 2.6f), 90f, new Vector3(0.55f, 1.0f, 0.55f), room, true);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(1.35f, 0f, 2.6f), -90f, new Vector3(0.55f, 1.0f, 0.55f), room, true);

            // Тарелка — сетевой объект: в неё «толкают» лапой.
            var plateGo = new GameObject("SausagePlate");
            plateGo.transform.position = center + Vector3.up * (top + 0.01f);
            var dish = ArtKit.Cylinder("Dish", plateGo.transform.position, 0.24f, 0.03f, ArtKit.Color("Dish", new Color(0.97f, 0.97f, 0.95f)), plateGo.transform, false);
            dish.isStatic = false;
            var rim = ArtKit.Color("DishRim", new Color(0.30f, 0.45f, 0.78f));
            ArtKit.Cylinder("DishRim", plateGo.transform.position + Vector3.up * 0.002f, 0.25f, 0.02f, rim, plateGo.transform, false).isStatic = false;
            var piles = new GameObject("Pile").transform;
            piles.SetParent(plateGo.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var p = Sausage("Piece", 0.2f, piles);
                float a = i * 1.3f;
                p.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.09f * (i > 0 ? 1 : 0), 0.06f + (i == 0 ? 0.04f : 0f), Mathf.Sin(a) * 0.09f * (i > 0 ? 1 : 0));
                p.transform.localRotation = Quaternion.Euler(0f, i * 53f, 0f);
            }
            var stickVisual = Object.Instantiate(stickTemplate, plateGo.transform);
            stickVisual.name = "Doctor";
            stickVisual.transform.localPosition = new Vector3(0f, 0.14f, -0.05f);
            stickVisual.SetActive(false);

            var box = plateGo.AddComponent<BoxCollider>();
            // Хватать можно с любого края стола: «тарелка» для толчка — вся столешница.
            box.center = new Vector3(0f, 0.12f, 0f);
            box.size = new Vector3(1.7f, 0.3f, 0.95f);
            var body = plateGo.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            plateGo.AddComponent<NetworkObject>();
            var plate = plateGo.AddComponent<SausagePlate>();
            var so = new SerializedObject(plate);
            so.FindProperty("stickVisual").objectReferenceValue = stickVisual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return plate;
        }

        // ───────────────────────── Укрытия ─────────────────────────

        private static void BuildCover(Transform room)
        {
            // Стопки коробок (выше головы кота — за ними не видно).
            BoxStack(room, new Vector3(-3.4f, 0f, 0.6f), 10f, 3);
            BoxStack(room, new Vector3(3.6f, 0f, -0.2f), -15f, 2);
            BoxStack(room, new Vector3(-5.9f, 0f, -2.6f), 25f, 2);
            ArtKit.PlaceFit("Furniture/washer", new Vector3(5.6f, 0f, 2.2f), -90f, new Vector3(0.9f, 0.95f, 0.75f), room, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(5.6f, 0.95f, 2.2f), 10f, new Vector3(0.7f, 0.55f, 0.7f), room, true);
            // Глухой буфет посреди кухни — главное укрытие на пути к мискам.
            Sideboard(room, new Vector3(0.4f, 0f, -1.5f));
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(-2.2f, 0f, -3.1f), 0f, new Vector3(0.42f, 0.6f, 0.42f), room, true);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(2.3f, 0f, -3.0f), 30f, new Vector3(0.55f, 1.0f, 0.55f), room, true);
        }

        private static void Sideboard(Transform room, Vector3 basePoint)
        {
            var wood = ArtKit.Color("SideboardWood", new Color(0.62f, 0.40f, 0.26f));
            var dark = ArtLibrary.WoodDark;
            var glass = ArtKit.Color("SideboardGlass", new Color(0.72f, 0.84f, 0.90f), 0.6f);
            var brass = ArtKit.Color("Brass", new Color(0.86f, 0.70f, 0.32f), 0.5f);
            Vector3 size = new Vector3(1.5f, 1.75f, 0.55f);
            ArtKit.Box("Sideboard", basePoint + Vector3.up * size.y * 0.5f, size, wood, room);
            ArtKit.Box("SideboardTop", basePoint + Vector3.up * (size.y + 0.03f), new Vector3(size.x + 0.1f, 0.06f, size.z + 0.08f), dark, room, false);
            foreach (float side in new[] { -1f, 1f })
            {
                // Нижние дверцы и стеклянные верхние — с обеих сторон (виден и с кухни, и от мисок).
                float z = basePoint.z + side * (size.z * 0.5f + 0.01f);
                ArtKit.Box("DoorLow", new Vector3(basePoint.x - 0.36f, 0.45f, z), new Vector3(0.66f, 0.7f, 0.02f), dark, room, false);
                ArtKit.Box("DoorLow", new Vector3(basePoint.x + 0.36f, 0.45f, z), new Vector3(0.66f, 0.7f, 0.02f), dark, room, false);
                ArtKit.Box("Glass", new Vector3(basePoint.x - 0.36f, 1.3f, z), new Vector3(0.6f, 0.65f, 0.02f), glass, room, false);
                ArtKit.Box("Glass", new Vector3(basePoint.x + 0.36f, 1.3f, z), new Vector3(0.6f, 0.65f, 0.02f), glass, room, false);
                ArtKit.Box("Knob", new Vector3(basePoint.x - 0.08f, 0.6f, z + side * 0.02f), new Vector3(0.04f, 0.06f, 0.03f), brass, room, false);
                ArtKit.Box("Knob", new Vector3(basePoint.x + 0.08f, 0.6f, z + side * 0.02f), new Vector3(0.04f, 0.06f, 0.03f), brass, room, false);
            }
        }

        private static void BoxStack(Transform room, Vector3 basePoint, float yaw, int count)
        {
            float y = 0f;
            for (int i = 0; i < count; i++)
            {
                float size = 0.9f - i * 0.15f;
                var box = ArtKit.PlaceFit(i % 2 == 0 ? "Furniture/cardboardBoxClosed" : "Furniture/cardboardBoxOpen",
                    basePoint + new Vector3(0f, y, 0f), yaw + i * 17f, new Vector3(size, size * 0.75f, size), room, true);
                y = box != null ? ArtKit.RendererBounds(box).max.y : y + size * 0.75f;
            }
        }

        // ───────────────────────── Миски ─────────────────────────

        private static Transform[] BuildBowls()
        {
            var bowlMesh = MaoshiSetupStage22.LatheMesh("CatBowl", new[]
            {
                new Vector2(0.15f, 0f), new Vector2(0.2f, 0.02f), new Vector2(0.25f, 0.08f), new Vector2(0.27f, 0.12f),
                new Vector2(0.235f, 0.125f), new Vector2(0.2f, 0.05f), new Vector2(0.001f, 0.045f),
            });
            var rimMesh = MaoshiSetupStage22.LatheMesh("CatBowlRim", new[] { new Vector2(0.255f, 0.085f), new Vector2(0.272f, 0.105f), new Vector2(0.276f, 0.125f) }, 0.004f, false);
            var steel = ArtKit.Color("BowlSteel", new Color(0.80f, 0.82f, 0.86f), 0.6f);
            var rimDefault = ArtKit.Color("BowlRim", new Color(0.9f, 0.5f, 0.3f));

            var parent = new GameObject("Bowls").transform;
            var bowls = new Transform[8];
            for (int i = 0; i < 8; i++)
            {
                var bowl = new GameObject($"Bowl{i}");
                bowl.transform.SetParent(parent, false);
                bowl.transform.position = new Vector3((i - 3.5f) * 1.5f, 0f, -5.2f);
                bowl.AddComponent<MeshFilter>().sharedMesh = bowlMesh;
                bowl.AddComponent<MeshRenderer>().sharedMaterial = steel;
                var rim = new GameObject("Rim");
                rim.transform.SetParent(bowl.transform, false);
                rim.AddComponent<MeshFilter>().sharedMesh = rimMesh;
                rim.AddComponent<MeshRenderer>().sharedMaterial = rimDefault;
                bowls[i] = bowl.transform;
            }
            // Коврик под миски.
            ArtKit.Patch("BowlMat", new Vector3(0f, 0.005f, -5.25f), new Vector2(12.5f, 1.0f), ArtKit.Color("BowlMat", new Color(0.36f, 0.55f, 0.62f)), 1f);
            return bowls;
        }
    }
}
