using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.SourCream;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 40: мини-игра «Кто съел сметану?».
    /// Деревенская кухня 12×9 м: в северо-западном углу кладовка за занавеской (там миска сметаны на ящике),
    /// посередине стол со стульями, у северной стены плита и шкафчики, в юго-восточном углу дверь —
    /// через неё возвращается хозяйка, перед дверью половик (умыться).
    /// Запуск: Maoshi → Setup → Stage 40 - Sour Cream. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage40
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/SourCream";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/SourCream.unity";
        private const float HalfX = 6f;
        private const float HalfZ = 4.5f;
        private const float WallHeight = 3f;

        [MenuItem("Maoshi/Setup/Stage 40 - Sour Cream")]
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/SourCream.asset");
            definition.EditorSetup("sour_cream", "Кто съел сметану?",
                "Хозяйка ушла, в кладовке миска сметаны. Лизни тайком (+2) — но усы испачкаешь. Умойся на половике или измажь другого кота лапой. Хозяйка вернётся — голосуйте, кто ел!",
                "SourCream", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 40 готов: «Кто съел сметану?» (в каталоге №{index + 1}).");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 6f, -8f), Quaternion.Euler(35f, 0f, 0f));

            var room = ArtKit.Group("Kitchen");
            BuildRoom(room);
            var (bowl, cream, glow) = BuildPantry(room);
            BuildKitchen(room);
            var rug = ArtKit.Patch("WashRug", new Vector3(4.6f, 0.005f, -3.5f), new Vector2(1.6f, 1.1f), ArtKit.Color("RugStripes", new Color(0.42f, 0.62f, 0.78f)), 1f, room, 0.03f);
            ArtKit.Label("RugHint", "умыться", new Vector3(4.6f, 0.05f, -3.5f), 0f, 0.05f, new Color(0.95f, 0.95f, 1f), room).transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Хозяйка: прячется за дверью, выходит, когда «вернулась».
            var hostess = new GameObject("Hostess");
            hostess.transform.SetPositionAndRotation(new Vector3(5.2f, 0f, -3.9f), Quaternion.Euler(0f, -20f, 0f));
            var body = new GameObject("Body").transform;
            body.SetParent(hostess.transform, false);
            MaoshiSetupStage38.AddHostessModel(body);
            ArtKit.Box("ShoppingBag", Vector3.zero, new Vector3(0.35f, 0.4f, 0.2f), ArtKit.Color("BagBrown", new Color(0.72f, 0.56f, 0.38f)), hostess.transform, false)
                .transform.localPosition = new Vector3(-0.38f, 0.7f, 0.05f);
            foreach (var t in hostess.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            var visualGo = new GameObject("SourCreamVisual");
            var visual = visualGo.AddComponent<SourCreamVisual>();
            var vso = new SerializedObject(visual);
            vso.FindProperty("hostess").objectReferenceValue = hostess.transform;
            vso.FindProperty("cream").objectReferenceValue = cream;
            vso.FindProperty("bowlGlow").objectReferenceValue = glow;
            vso.FindProperty("creamMaterial").objectReferenceValue = ArtKit.Color("SourCream", new Color(0.99f, 0.98f, 0.93f), 0.6f);
            vso.FindProperty("blobMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            vso.ApplyModifiedPropertiesWithoutUndo();

            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0.6f, 0f, -2.6f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var sso = new SerializedObject(spawner);
            sso.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            sso.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            sso.FindProperty("lineSpacing").floatValue = 1.2f;
            sso.FindProperty("spawnHeight").floatValue = 0.3f;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("SourCreamMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<SourCreamMode>();
            var so = new SerializedObject(mode);
            so.FindProperty("miniGameId").stringValue = "sour_cream";
            so.FindProperty("timeLimitSeconds").floatValue = 0f;
            so.FindProperty("endWhenOnePlayerLeft").boolValue = false;
            so.FindProperty("bowl").objectReferenceValue = bowl;
            so.FindProperty("rug").objectReferenceValue = rug.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Lighting(warm: true, 30f, 110f);
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        private static void NoShadow(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void BuildRoom(Transform room)
        {
            var floor = ArtLibrary.Planks;
            var wall = ArtKit.Color("KitchenGreen", new Color(0.72f, 0.82f, 0.66f));
            var trim = ArtKit.Color("Skirting", new Color(0.93f, 0.89f, 0.80f));
            float t = 0.3f;
            ArtKit.UvBox("Floor", new Vector3(0f, -0.3f, 0f), new Vector3(HalfX * 2f, 0.3f, HalfZ * 2f), floor, floor, new Vector2(1f, 1f), 1f, room);
            NoShadow(ArtKit.UvBox("WallNorth", new Vector3(0f, 0f, HalfZ + t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wall, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallSouth", new Vector3(0f, 0f, -HalfZ - t * 0.5f), new Vector3(HalfX * 2f + t * 2f, WallHeight, t), wall, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallWest", new Vector3(-HalfX - t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wall, trim, new Vector2(1f, 1f), 1f, room));
            NoShadow(ArtKit.UvBox("WallEast", new Vector3(HalfX + t * 0.5f, 0f, 0f), new Vector3(t, WallHeight, HalfZ * 2f), wall, trim, new Vector2(1f, 1f), 1f, room));
            var plaster = ArtKit.Color("CeilingPlaster", new Color(0.96f, 0.94f, 0.89f));
            NoShadow(ArtKit.UvBox("Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(HalfX * 2f + t * 2f, 0.25f, HalfZ * 2f + t * 2f), plaster, plaster, new Vector2(2f, 2f), 2f, room));
            MaoshiSetupStage22.Chandelier(room, new Vector3(0.5f, WallHeight, 0.3f), 3f, 0.08f);

            // Дверь в прихожую (юго-восток) — оттуда приходит хозяйка.
            var doorWood = ArtKit.Color("DoorWood", new Color(0.55f, 0.36f, 0.22f));
            ArtKit.Box("DoorDark", new Vector3(5.2f, 1.05f, -HalfZ + 0.01f), new Vector3(1.05f, 2.1f, 0.04f), ArtKit.Color("Hallway", new Color(0.30f, 0.24f, 0.22f)), room, false);
            ArtKit.Box("DoorFrame", new Vector3(5.2f, 2.15f, -HalfZ + 0.03f), new Vector3(1.25f, 0.1f, 0.06f), trim, room, false);
            ArtKit.Box("DoorLeaf", new Vector3(4.62f, 1.05f, -HalfZ + 0.48f), new Vector3(0.05f, 2.1f, 0.9f), doorWood, room, false);

            // Окно на восточной стене.
            var sky = ArtKit.Color("WindowSky", new Color(0.62f, 0.78f, 0.96f));
            ArtKit.Box("WindowGlass", new Vector3(HalfX - 0.01f, 1.7f, 1.2f), new Vector3(0.02f, 1.2f, 1.6f), sky, room, false);
            ArtKit.Box("WindowSill", new Vector3(HalfX - 0.1f, 1.08f, 1.2f), new Vector3(0.2f, 0.05f, 1.8f), trim, room, false);
        }

        private static (Transform bowl, Transform cream, Transform glow) BuildPantry(Transform room)
        {
            // Кладовка: две стены, вход с востока, занавеска (без коллайдера).
            var plank = ArtKit.Color("PantryWood", new Color(0.62f, 0.45f, 0.30f));
            const float h = 2.3f;
            ArtKit.UvBox("PantrySouth", new Vector3(-4.7f, 0f, 1.5f), new Vector3(2.6f, h, 0.15f), plank, plank, new Vector2(1f, 1f), 1f, room);
            ArtKit.UvBox("PantryEastA", new Vector3(-3.4f, 0f, 1.95f), new Vector3(0.15f, h, 0.9f), plank, plank, new Vector2(1f, 1f), 1f, room);
            ArtKit.UvBox("PantryEastB", new Vector3(-3.4f, 0f, 4.0f), new Vector3(0.15f, h, 1.0f), plank, plank, new Vector2(1f, 1f), 1f, room);
            ArtKit.Box("PantryTop", new Vector3(-4.7f, h + 0.04f, 3.0f), new Vector3(2.7f, 0.08f, 3.1f), plank, room, false);
            var curtain = ArtKit.Color("PantryCurtain", new Color(0.86f, 0.38f, 0.36f));
            ArtKit.Box("PantryCurtain", new Vector3(-3.4f, 1.3f, 2.95f), new Vector3(0.04f, 1.4f, 1.1f), curtain, room, false);
            ArtKit.Label("PantrySign", "КЛАДОВКА", new Vector3(-3.3f, 2.1f, 2.95f), -90f, 0.035f, new Color(0.95f, 0.92f, 0.85f), room);

            // Полки с банками.
            var wood = ArtLibrary.Wood;
            for (int s = 0; s < 3; s++)
            {
                float y = 0.6f + s * 0.55f;
                ArtKit.Box("Shelf", new Vector3(-4.7f, y, HalfZ - 0.2f), new Vector3(2.4f, 0.04f, 0.35f), wood, room, false);
                Color[] jars = { new(0.85f, 0.55f, 0.25f), new(0.55f, 0.75f, 0.45f), new(0.9f, 0.85f, 0.6f), new(0.75f, 0.35f, 0.35f) };
                for (int j = 0; j < 4; j++)
                    ArtKit.Cylinder("Jar", new Vector3(-5.6f + j * 0.55f + s * 0.1f, y + 0.02f, HalfZ - 0.2f), 0.09f, 0.22f, ArtKit.Color($"Jar{j}", jars[j]), room, false);
            }

            // Миска сметаны на низком ящике.
            ArtKit.Box("BowlCrate", new Vector3(-4.8f, 0.15f, 3.4f), new Vector3(0.8f, 0.3f, 0.6f), ArtLibrary.Wood, room);
            var bowlMesh = MaoshiSetupStage22.LatheMesh("CreamBowl", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.24f, 0.02f), new Vector2(0.3f, 0.09f), new Vector2(0.32f, 0.13f),
                new Vector2(0.29f, 0.135f), new Vector2(0.24f, 0.05f), new Vector2(0.001f, 0.045f),
            });
            var bowl = new GameObject("CreamBowl");
            bowl.transform.SetParent(room, false);
            bowl.transform.position = new Vector3(-4.8f, 0.3f, 3.4f);
            bowl.AddComponent<MeshFilter>().sharedMesh = bowlMesh;
            bowl.AddComponent<MeshRenderer>().sharedMaterial = ArtKit.Color("BowlBlue", new Color(0.36f, 0.52f, 0.82f), 0.5f);
            bowl.isStatic = false;
            var cream = ArtKit.Cylinder("Cream", Vector3.zero, 0.27f, 0.02f, ArtKit.Color("SourCream", new Color(0.99f, 0.98f, 0.93f), 0.6f), bowl.transform, false);
            cream.isStatic = false;
            cream.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            var spoon = ArtKit.Box("Spoon", Vector3.zero, new Vector3(0.04f, 0.015f, 0.3f), ArtLibrary.MetalDark, bowl.transform, false);
            spoon.isStatic = false;
            spoon.transform.localPosition = new Vector3(0.12f, 0.14f, 0f);
            spoon.transform.localRotation = Quaternion.Euler(-15f, 30f, 0f);

            var glow = new GameObject("BowlGlow");
            glow.transform.SetParent(room, false);
            glow.transform.position = new Vector3(-4.8f, 0.32f, 3.4f);
            glow.transform.localScale = Vector3.one * 0.6f;
            glow.AddComponent<MeshFilter>().sharedMesh = MaoshiSetupStage38.RingMesh();
            var glowRenderer = glow.AddComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = MaoshiSetupStage38.AdditiveMaterial("BowlGlow", new Color(1f, 0.95f, 0.7f, 0.8f));
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow.SetActive(false);
            return (bowl.transform, cream.transform, glow.transform);
        }

        private static void BuildKitchen(Transform room)
        {
            float z = HalfZ - 0.4f;
            ArtKit.PlaceFit("Furniture/kitchenStove", new Vector3(0.6f, 0f, z), 180f, new Vector3(0.9f, 0.95f, 0.75f), room, true);
            for (int i = 1; i <= 2; i++)
            {
                ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(0.6f - i * 0.95f, 0f, z), 180f, new Vector3(0.95f, 0.95f, 0.75f), room, true);
                ArtKit.PlaceFit("Furniture/kitchenCabinet", new Vector3(0.6f + i * 0.95f, 0f, z), 180f, new Vector3(0.95f, 0.95f, 0.75f), room, true);
            }
            ArtKit.PlaceFit("Furniture/kitchenFridge", new Vector3(4.2f, 0f, z), 180f, new Vector3(0.95f, 2.0f, 0.8f), room, true);
            ArtKit.PlaceFit("Furniture/washer", new Vector3(HalfX - 0.45f, 0f, -0.6f), -90f, new Vector3(0.9f, 0.95f, 0.75f), room, true);

            // Стол посередине: коллайдер — только столешница (под столом можно спрятаться).
            var table = ArtKit.PlaceFit("Furniture/table", new Vector3(0.6f, 0f, 0.4f), 0f, new Vector3(1.9f, 0.78f, 1.0f), room);
            if (table != null)
            {
                var b = ArtKit.RendererBounds(table);
                var slab = new GameObject("TableTop");
                slab.transform.SetParent(room, false);
                slab.transform.position = new Vector3(b.center.x, b.max.y - 0.05f, b.center.z);
                slab.AddComponent<BoxCollider>().size = new Vector3(b.size.x, 0.1f, b.size.z);
            }
            ArtKit.Box("Tablecloth", new Vector3(0.6f, 0.79f, 0.4f), new Vector3(1.3f, 0.01f, 0.8f), ArtKit.Color("Tablecloth", new Color(0.95f, 0.92f, 0.86f)), room, false);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(-0.75f, 0f, 0.4f), 90f, new Vector3(0.55f, 1.0f, 0.55f), room, true);
            ArtKit.PlaceFit("Furniture/chair", new Vector3(1.95f, 0f, 0.4f), -90f, new Vector3(0.55f, 1.0f, 0.55f), room, true);

            // Укрытия и мелочи.
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(-1.6f, 0f, -2.9f), 15f, new Vector3(0.8f, 0.6f, 0.8f), room, true);
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(-5.4f, 0f, -3.8f), 0f, new Vector3(0.45f, 0.65f, 0.45f), room, true);
            ArtKit.PlaceFit("Furniture/pottedPlant", new Vector3(3.2f, 0f, 1.6f), 0f, new Vector3(0.55f, 1.1f, 0.55f), room, true);
            ArtKit.PlaceFit("Furniture/radio", new Vector3(1.55f, 0.95f, z), 180f, new Vector3(0.4f, 0.3f, 0.25f), room);
        }
    }
}
