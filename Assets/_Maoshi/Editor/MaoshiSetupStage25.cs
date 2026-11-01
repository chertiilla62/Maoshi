using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Gameplay;
using Maoshi.MiniGames.NightConcert;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 25: мини-игра «Ночной концерт».
    /// Ночной двор под пятиэтажкой: луна, фонари, светящиеся окна (из них летят ботинки), мусорные баки за спиной у котов.
    /// Коты стоят в ряд лицом к дому. Музыка concert.wav (распакована в память — точный старт по dspTime) и карта нот concert_chart.txt.
    /// Запуск: Maoshi → Setup → Stage 25 - Night Concert. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage25
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/NightConcert";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/NightConcert.unity";
        public const string MusicPath = "Assets/_Maoshi/Resources/Maoshi/Audio/Music/concert.wav";
        private const string ChartPath = "Assets/_Maoshi/Resources/Maoshi/Concert/concert_chart.txt";

        [MenuItem("Maoshi/Setup/Stage 25 - Night Concert")]
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

            ConfigureMusic();
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            var chart = AssetDatabase.LoadAssetAtPath<TextAsset>(ChartPath);
            if (music == null || chart == null)
            {
                Debug.LogError("[Maoshi Setup] Нет музыки или карты нот концерта.");
                return;
            }

            MaoshiSetup.EnsureFolder(Folder);
            var catalog = MaoshiSetupStage3.GetOrCreate<MiniGameCatalog>(MaoshiSetupStage3.CatalogPath);
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/NightConcert.asset");
            definition.EditorSetup("night_concert", "Ночной концерт",
                "Ночью под окнами коты поют в такт: E — «мяу», Q — «шшш». Серия нот даёт ботинок — кинь его в лидера! Кто спел лучше всех, тот звезда двора.",
                "NightConcert", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab, music, chart);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            int index = -1;
            for (int i = 0; i < catalog.MiniGames.Count; i++) if (catalog.MiniGames[i] == definition) index = i;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            Debug.Log($"[Maoshi Setup] Этап 25 готов: «Ночной концерт» (в каталоге №{index + 1}; во дворе в редакторе — клавиша {(index >= 0 && index < keys.Length ? keys[index] : "—")}).");
        }

        /// <summary>Музыку концерта держим распакованной в памяти: потоковая дорожка стартует с задержкой и ноты разъедутся.</summary>
        public static void ConfigureMusic()
        {
            if (AssetImporter.GetAtPath(MusicPath) is not AudioImporter importer) return;
            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.DecompressOnLoad && !importer.loadInBackground) return;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        private static void CreateScene(GameObject catPrefab, AudioClip music, TextAsset chart)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null) camera.transform.SetPositionAndRotation(new Vector3(0f, 2.5f, -7f), Quaternion.Euler(8f, 0f, 0f));

            var root = ArtKit.Group("Yard");

            // Двор: асфальт, тротуар у дома, бордюр.
            ArtKit.UvBox("Ground", new Vector3(0f, -0.3f, 2f), new Vector3(44f, 0.3f, 34f), ArtLibrary.Asphalt, ArtLibrary.Asphalt, new Vector2(5f, 5f), 5f, root);
            ArtKit.Patch("Sidewalk", new Vector3(0f, 0.01f, 3.8f), new Vector2(34f, 2.4f), ArtLibrary.Sidewalk, 2f, root);

            // Дом: пятиэтажка, у неё горят окна.
            float floor = ArtLibrary.Floor;
            ArtLibrary.House(root, new Vector3(0f, 0f, 9f), new Vector3(34f, floor * 5f, 8f), ArtLibrary.FacadePanel);
            var windows = BuildWindows(root, floor);

            // Соседние дома по бокам и забор сзади.
            ArtLibrary.House(root, new Vector3(-22f, 0f, 2f), new Vector3(8f, floor * 5f, 26f), ArtLibrary.FacadeBrick);
            ArtLibrary.House(root, new Vector3(22f, 0f, 2f), new Vector3(8f, floor * 4f, 26f), ArtLibrary.FacadeBlue);
            for (float x = -16f; x <= 16f; x += 2.02f)
                ArtKit.PlaceFit("Graveyard/ironFence", new Vector3(x, 0f, -8f), 0f, new Vector3(2.02f, 1.7f, 0.2f), root);

            // За спинами котов — баки, коробки, скамейка: «сцена» во дворе.
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(-7.5f, 0f, -2.2f), 10f, new Vector3(0.6f, 0.9f, 0.6f), root, true);
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(7.2f, 0f, -2.0f), -20f, new Vector3(0.6f, 0.9f, 0.6f), root, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxOpen", new Vector3(-8.6f, 0f, -0.6f), 25f, new Vector3(0.9f, 0.7f, 0.9f), root, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(8.6f, 0f, -0.4f), -10f, new Vector3(0.8f, 0.7f, 0.8f), root, true);
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(0f, 0f, -6.2f), 0f, new Vector3(2f, 1.3f, 0.9f), root, true);
            ArtKit.PlaceFit("Food/fishBones", new Vector3(-2.5f, 0.02f, -4.8f), 40f, new Vector3(0.5f, 0.2f, 0.5f), root);

            // Фонари — тёплые пятна света.
            foreach (float x in new[] { -10f, 10f })
            {
                ArtLibrary.Lamp(root, new Vector3(x, 0f, 3.6f));
                var go = new GameObject("LampLight");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(x, 4f, 3.4f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.78f, 0.45f);
                light.range = 11f;
                light.intensity = 3.2f;
                light.shadows = LightShadows.None;
            }

            // Луна и звёзды.
            var moonMaterial = ArtKit.Color("Moon", new Color(1f, 0.96f, 0.82f));
            moonMaterial.SetColor("_EmissionColor", new Color(1.2f, 1.1f, 0.85f));
            moonMaterial.EnableKeyword("_EMISSION");
            var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moon.name = "Moon";
            Object.DestroyImmediate(moon.GetComponent<Collider>());
            moon.transform.SetParent(root, false);
            moon.transform.position = new Vector3(-14f, 26f, 48f);
            moon.transform.localScale = Vector3.one * 5f;
            moon.GetComponent<Renderer>().sharedMaterial = moonMaterial;
            moon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var starMaterial = ArtKit.Color("Star", new Color(1f, 1f, 0.9f));
            starMaterial.SetColor("_EmissionColor", new Color(1f, 1f, 0.85f));
            starMaterial.EnableKeyword("_EMISSION");
            var rng = new System.Random(7);
            for (int i = 0; i < 70; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Quad);
                star.name = "Star";
                Object.DestroyImmediate(star.GetComponent<Collider>());
                star.transform.SetParent(root, false);
                float x = (float)(rng.NextDouble() * 120 - 60);
                float y = 18f + (float)(rng.NextDouble() * 30);
                star.transform.position = new Vector3(x, y, 55f + (float)rng.NextDouble() * 10f);
                star.transform.localScale = Vector3.one * (0.15f + (float)rng.NextDouble() * 0.2f);
                var r = star.GetComponent<Renderer>();
                r.sharedMaterial = starMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Коты в ряд лицом к дому.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -2.5f), Quaternion.identity);
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 1.6f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var modeGo = new GameObject("ConcertMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<ConcertMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "night_concert";
            serialized.FindProperty("timeLimitSeconds").floatValue = 0f;
            serialized.FindProperty("chart").objectReferenceValue = chart;
            serialized.FindProperty("music").objectReferenceValue = music;
            var windowsProperty = serialized.FindProperty("windows");
            windowsProperty.arraySize = windows.Count;
            for (int i = 0; i < windows.Count; i++) windowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = windows[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Сначала день (небо, камера), потом ночь поверх. Солнце ищем явно: в сцене есть и фонари.
            ArtLibrary.Lighting(warm: false, 25f, 95f);
            Night(camera);
            MaoshiSetupStage22.ConvertSceneMaterials();
            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>Светящиеся окна на фасаде (половина — точки, откуда летят ботинки) и тёмные стёкла.</summary>
        private static List<Transform> BuildWindows(Transform root, float floor)
        {
            var lit = ArtKit.Color("WindowLit", new Color(1f, 0.82f, 0.45f));
            lit.SetColor("_EmissionColor", new Color(1.4f, 1.0f, 0.45f));
            lit.EnableKeyword("_EMISSION");
            var curtain = ArtKit.Color("WindowCurtainLit", new Color(0.95f, 0.55f, 0.35f));
            curtain.SetColor("_EmissionColor", new Color(0.7f, 0.3f, 0.15f));
            curtain.EnableKeyword("_EMISSION");
            var frame = ArtKit.Color("WindowFrame", new Color(0.96f, 0.95f, 0.92f));

            var result = new List<Transform>();
            float z = 4.96f;
            float[] xs = { -12f, -7.5f, -3f, 1.5f, 6f, 10.5f };
            for (int level = 1; level <= 3; level++)
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    bool on = (i + level) % 2 == 0 || (level == 2 && i == 3);
                    if (!on) continue;
                    float y = floor * level + 1.4f;
                    var window = ArtKit.Box("LitWindow", new Vector3(xs[i], y, z), new Vector3(1.3f, 1.4f, 0.04f), lit, root, false);
                    ArtKit.Box("Curtain", new Vector3(xs[i] - 0.42f, y, z - 0.03f), new Vector3(0.36f, 1.4f, 0.02f), curtain, root, false);
                    ArtKit.Box("Frame", new Vector3(xs[i], y - 0.74f, z - 0.05f), new Vector3(1.5f, 0.08f, 0.12f), frame, root, false);
                    var point = new GameObject("BootWindow");
                    point.transform.SetParent(root, false);
                    point.transform.position = new Vector3(xs[i], y, z - 0.4f);
                    result.Add(point.transform);
                    window.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return result;
        }

        /// <summary>Ночь: лунный свет, тёмно-синее небо и туман, приглушённый окружающий свет.</summary>
        internal static void Night(Camera camera)
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
                sun.color = new Color(0.62f, 0.70f, 1f);
                sun.intensity = 0.45f;
                sun.shadowStrength = 0.7f;
            }

            string path = ArtKit.MaterialsFolder + "/M_SkyNight.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetFloat("_SunSize", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.4f);
            sky.SetColor("_SkyTint", new Color(0.10f, 0.12f, 0.30f));
            sky.SetColor("_GroundColor", new Color(0.05f, 0.05f, 0.08f));
            sky.SetFloat("_Exposure", 0.32f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.24f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.18f);
            RenderSettings.fogColor = new Color(0.08f, 0.10f, 0.22f);
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 110f;
            DynamicGI.UpdateEnvironment();
        }
    }
}
