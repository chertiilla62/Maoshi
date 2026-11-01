using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.HotPie;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 39: мини-игра «Горячий пирожок».
    /// Двор у пекарни: площадка 16×16 м за высоким дощатым забором (не перепрыгнуть), посередине лоток
    /// с пирожками и бабушка, вокруг укрытия, чтобы бегать кругами: бочка кваса, ящики, лавка, дерево.
    /// Выбывшие — на скамейку за южным забором (смотрят и кидают тапки).
    /// Запуск: Maoshi → Setup → Stage 39 - Hot Pie. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage39
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/HotPie";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/HotPie.unity";
        private const float Half = 8f;
        private const float FenceHeight = 1.7f;

        [MenuItem("Maoshi/Setup/Stage 39 - Hot Pie")]
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/HotPie.asset");
            definition.EditorSetup("hot_pie", "Горячий пирожок",
                "Бабушка кидает раскалённый пирожок. Передай его толчком, пока он не сделал «пуф»! У кого взорвался — выбыл, последний оставшийся побеждает.",
                "HotPie", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            Debug.Log($"[Maoshi Setup] Этап 39 готов: «Горячий пирожок» (в каталоге №{index + 1}).");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 9f, -13f), Quaternion.Euler(35f, 0f, 0f));

            var yard = ArtKit.Group("BakeryYard");
            ArtKit.UvBox("Ground", new Vector3(0f, -0.3f, 0f), new Vector3(Half * 2f + 10f, 0.3f, Half * 2f + 10f), ArtLibrary.Asphalt, ArtLibrary.Sidewalk, new Vector2(4f, 4f), 2.5f, yard);

            // Забор выше прыжка кота.
            var planks = ArtLibrary.Planks;
            ArtKit.UvBox("FenceNorth", new Vector3(0f, 0f, Half + 0.1f), new Vector3(Half * 2f + 0.4f, FenceHeight, 0.2f), planks, ArtLibrary.WoodDark, new Vector2(1f, 1f), 1f, yard);
            ArtKit.UvBox("FenceSouth", new Vector3(0f, 0f, -Half - 0.1f), new Vector3(Half * 2f + 0.4f, FenceHeight, 0.2f), planks, ArtLibrary.WoodDark, new Vector2(1f, 1f), 1f, yard);
            ArtKit.UvBox("FenceWest", new Vector3(-Half - 0.1f, 0f, 0f), new Vector3(0.2f, FenceHeight, Half * 2f), planks, ArtLibrary.WoodDark, new Vector2(1f, 1f), 1f, yard);
            ArtKit.UvBox("FenceEast", new Vector3(Half + 0.1f, 0f, 0f), new Vector3(0.2f, FenceHeight, Half * 2f), planks, ArtLibrary.WoodDark, new Vector2(1f, 1f), 1f, yard);
            // Невидимый борт поверх забора — чтобы не запрыгнуть с ящиков.
            foreach (var (pos, size) in new[]
                     {
                         (new Vector3(0f, FenceHeight + 1f, Half + 0.1f), new Vector3(Half * 2f, 2f, 0.2f)),
                         (new Vector3(0f, FenceHeight + 1f, -Half - 0.1f), new Vector3(Half * 2f, 2f, 0.2f)),
                         (new Vector3(-Half - 0.1f, FenceHeight + 1f, 0f), new Vector3(0.2f, 2f, Half * 2f)),
                         (new Vector3(Half + 0.1f, FenceHeight + 1f, 0f), new Vector3(0.2f, 2f, Half * 2f)),
                     })
            {
                var wall = new GameObject("InvisibleWall");
                wall.transform.SetParent(yard, false);
                wall.transform.position = pos;
                wall.AddComponent<BoxCollider>().size = size;
            }

            // Пекарня за северным забором (фасад с вывеской).
            ArtKit.UvBox("Bakery", new Vector3(0f, 0f, Half + 4f), new Vector3(12f, ArtLibrary.Floor * 3f, 6f), ArtLibrary.FacadeBrick, ArtLibrary.RoofTar, new Vector2(6f, ArtLibrary.Floor * 2f), 4f, yard, false);
            ArtKit.Box("SignBoard", new Vector3(0f, 3.4f, Half + 0.95f), new Vector3(4.2f, 0.9f, 0.1f), ArtKit.Color("BakerySign", new Color(0.92f, 0.78f, 0.45f)), yard, false);
            ArtKit.Label("SignText", "ПИРОЖКИ", new Vector3(0f, 3.4f, Half + 0.88f), 0f, 0.11f, new Color(0.45f, 0.22f, 0.12f), yard);

            // Лоток с пирожками в центре и бабушка за ним.
            var cartWood = ArtKit.Color("CartWood", new Color(0.70f, 0.48f, 0.28f));
            var cloth = ArtKit.Color("CartCloth", new Color(0.92f, 0.32f, 0.30f));
            ArtKit.Box("Cart", new Vector3(0f, 0.5f, 0.6f), new Vector3(1.8f, 1.0f, 0.9f), cartWood, yard);
            ArtKit.Box("CartTop", new Vector3(0f, 1.03f, 0.6f), new Vector3(1.9f, 0.06f, 1.0f), cloth, yard, false);
            for (int i = 0; i < 7; i++)
                PieModel(yard, new Vector3(-0.7f + i * 0.23f, 1.1f, 0.5f + (i % 2) * 0.18f), i * 25f, false);
            foreach (float x in new[] { -0.85f, 0.85f })
                ArtKit.Box("UmbrellaPole", new Vector3(x, 1.5f, 1.0f), new Vector3(0.05f, 1.0f, 0.05f), ArtLibrary.MetalDark, yard, false);
            ArtKit.Box("Awning", new Vector3(0f, 2.05f, 0.85f), new Vector3(2.1f, 0.08f, 1.2f), ArtKit.Color("AwningStripes", new Color(0.96f, 0.92f, 0.86f)), yard, false);

            var grandma = new GameObject("Grandma");
            grandma.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1.5f), Quaternion.Euler(0f, 180f, 0f));
            var gCollider = grandma.AddComponent<CapsuleCollider>();
            gCollider.center = new Vector3(0f, 0.9f, 0f);
            gCollider.height = 1.8f;
            gCollider.radius = 0.32f;
            var body = new GameObject("Body").transform;
            body.SetParent(grandma.transform, false);
            MaoshiSetupStage38.AddHostessModel(body);
            var hand = new GameObject("Hand").transform;
            hand.SetParent(grandma.transform, false);
            hand.localPosition = new Vector3(0.3f, 1.4f, 0.4f);
            foreach (var t in grandma.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            // Укрытия — бегать кругами.
            ArtKit.Cylinder("KvassBarrel", new Vector3(4.5f, 0f, 3.5f), 0.55f, 1.1f, ArtKit.Color("KvassYellow", new Color(0.95f, 0.75f, 0.20f)), yard);
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(-4.6f, 0f, 3.2f), 15f, new Vector3(1.0f, 0.8f, 1.0f), yard, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxOpen", new Vector3(-4.0f, 0f, 4.4f), -20f, new Vector3(0.8f, 0.6f, 0.8f), yard, true);
            var bench = ArtKit.PlaceFit("Graveyard/bench", new Vector3(-4.2f, 0f, -3.6f), 90f, new Vector3(1.8f, 0.5f, 0.5f), yard, true);
            if (bench == null) ArtKit.Box("BenchBlock", new Vector3(-4.2f, 0.25f, -3.6f), new Vector3(0.5f, 0.5f, 1.8f), ArtLibrary.Wood, yard);
            ArtLibrary.Tree(yard, "Nature/tree_oak", new Vector3(4.6f, 0f, -3.8f), 5.5f, true);
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(7.2f, 0f, 7.2f), 0f, new Vector3(0.5f, 0.75f, 0.5f), yard, true);
            ArtKit.Box("Pallet", new Vector3(0f, 0.08f, -5.2f), new Vector3(1.4f, 0.16f, 1.0f), ArtLibrary.Wood, yard);

            // Скамейка выбывших за южным забором.
            var spectators = new GameObject("Bench");
            spectators.transform.SetPositionAndRotation(new Vector3(0f, 0.2f, -Half - 2.2f), Quaternion.identity);
            ArtKit.Box("SpectatorBench", new Vector3(0f, 0.25f, -Half - 2.8f), new Vector3(6f, 0.5f, 0.6f), ArtLibrary.Wood, yard);

            // Шаблон пирожка над головой (виден всем) и материалы пара/муки.
            var pieRoot = new GameObject("HotPie");
            var pieBody = PieModel(pieRoot.transform, Vector3.zero, 0f, true);
            foreach (var t in pieRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            var visualGo = new GameObject("HotPieVisual");
            var visual = visualGo.AddComponent<HotPieVisual>();
            var vso = new SerializedObject(visual);
            vso.FindProperty("pie").objectReferenceValue = pieRoot.transform;
            var renderers = vso.FindProperty("pieRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = pieBody;
            vso.FindProperty("grandma").objectReferenceValue = grandma.transform;
            vso.FindProperty("grandmaHand").objectReferenceValue = hand;
            vso.FindProperty("steam").objectReferenceValue = MaoshiSetupStage38.AdditiveMaterial("Steam", new Color(1f, 1f, 1f, 0.55f), true, false);
            vso.FindProperty("flour").objectReferenceValue = ArtKit.Color("FlourPuff", new Color(0.97f, 0.95f, 0.90f));
            vso.FindProperty("puffMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            vso.ApplyModifiedPropertiesWithoutUndo();

            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -0.3f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var sso = new SerializedObject(spawner);
            sso.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            sso.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Circle;
            var radius = sso.FindProperty("spawnRadius");
            if (radius != null) radius.floatValue = 5f;
            sso.FindProperty("spawnHeight").floatValue = 0.3f;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("HotPieMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<HotPieMode>();
            var so = new SerializedObject(mode);
            so.FindProperty("miniGameId").stringValue = "hot_pie";
            so.FindProperty("timeLimitSeconds").floatValue = 0f;
            so.FindProperty("bench").objectReferenceValue = spectators.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            ArtLibrary.Surround(new Vector2(-Half - 3f, -Half - 4f), new Vector2(Half + 3f, Half + 8f), 6f, 39, 0f, true);
            ArtLibrary.Lighting();
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>Пирожок: румяный «лодочкой» с защипом сверху. Возвращает рендер тела (его цвет меняется от жара).</summary>
        private static Renderer PieModel(Transform parent, Vector3 position, float yaw, bool big)
        {
            float k = big ? 1.6f : 1f;
            var root = new GameObject("Pie").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var crust = ArtKit.Color("PieCrust", new Color(0.86f, 0.58f, 0.28f));
            var seam = ArtKit.Color("PieSeam", new Color(0.62f, 0.36f, 0.16f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Crust";
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(0.24f, 0.11f, 0.15f) * k;
            body.GetComponent<Renderer>().sharedMaterial = crust;
            var ridge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ridge.GetComponent<Collider>());
            ridge.name = "Seam";
            ridge.transform.SetParent(root, false);
            ridge.transform.localPosition = new Vector3(0f, 0.045f, 0f) * k;
            ridge.transform.localScale = new Vector3(0.2f, 0.035f, 0.035f) * k;
            ridge.GetComponent<Renderer>().sharedMaterial = seam;
            return body.GetComponent<Renderer>();
        }
    }
}
