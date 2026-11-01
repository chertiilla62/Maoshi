using System.Collections.Generic;
using Maoshi.Gameplay;
using Maoshi.MiniGames.PigeonRace;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 15: мини-игра «Гонка по крышам за голубем».
    /// Шесть крыш подряд вдоль +Z с провалами 2–2.6 м, антеннами, трубой, кондиционерами,
    /// доской над широким провалом и голубятней в конце. Голубь перелетает с насеста на насест.
    /// Запуск: Maoshi → Setup → Stage 15 - Pigeon Race. Сцена пересоздаётся.
    /// </summary>
    public static class MaoshiSetupStage15
    {
        private const string Folder = MaoshiSetupStage3.MiniGamesFolder + "/PigeonRace";
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/PigeonRace.unity";
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";

        /// <summary>Крыша: начало и конец по Z, ширина, высота верха, сдвиг по X.</summary>
        private struct RoofSpec
        {
            public float Z0, Z1, Width, Top, X;
            public RoofSpec(float z0, float z1, float width, float top, float x)
            {
                Z0 = z0; Z1 = z1; Width = width; Top = top; X = x;
            }
        }

        private static readonly RoofSpec[] Roofs =
        {
            new(0f, 12f, 8f, 8f, 0f),        // старт
            new(14f, 24f, 6f, 8.6f, 0.5f),   // антенны
            new(26.4f, 34f, 5f, 8f, -0.5f),  // труба, дальше доска
            new(39f, 49f, 7f, 9f, 0f),       // кондиционеры и барьер
            new(51.6f, 58f, 4f, 8.4f, 1f),   // узкая
            new(60.2f, 70f, 8f, 8.4f, 0f),   // голубятня
        };

        [MenuItem("Maoshi/Setup/Stage 15 - Pigeon Race")]
        public static void SetupStage15()
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
            var definition = MaoshiSetupStage3.GetOrCreate<MiniGameDefinition>(Folder + "/PigeonRace.asset");
            definition.EditorSetup("pigeon_race", "Гонка за голубем",
                "Гонка по крышам на места! Беги за голубем через провалы и ловушки, первый у голубятни хватает его. Упал — вернёшься назад.",
                "PigeonRace", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
            EditorUtility.SetDirty(definition);
            catalog.EditorAdd(definition);
            EditorUtility.SetDirty(catalog);

            CreateScene(catPrefab);
            MaoshiSetupStage3.AddSceneToBuild(ScenePath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 15 готов: «Гонка за голубем».");
        }

        private static void CreateScene(GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(-22f, 24f, 20f), Quaternion.Euler(35f, 70f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.66f, 0.82f, 0.96f);
            }

            Material ground = MaoshiSetupStage3.LoadMaterial("Ground");
            Material street = GetOrCreateMaterial("Asphalt", new Color(0.40f, 0.40f, 0.43f));
            Material roofTar = GetOrCreateMaterial("RoofTar", new Color(0.36f, 0.34f, 0.38f));
            Material metal = GetOrCreateMaterial("RoofMetal", new Color(0.62f, 0.64f, 0.68f));
            Material chimney = GetOrCreateMaterial("Chimney", new Color(0.55f, 0.30f, 0.24f));
            Material wood = GetOrCreateMaterial("BenchWood", new Color(0.62f, 0.42f, 0.24f));
            Material[] walls =
            {
                MaoshiSetupStage3.LoadMaterial("Brick"),
                GetOrCreateMaterial("WallCream", new Color(0.90f, 0.82f, 0.66f)),
                GetOrCreateMaterial("WallTeal", new Color(0.42f, 0.62f, 0.62f)),
                GetOrCreateMaterial("WallPink", new Color(0.85f, 0.62f, 0.60f)),
            };

            // Улица внизу.
            MaoshiSetupStage3.Block("Ground", new Vector3(0f, -0.5f, 35f), new Vector3(60f, 1f, 110f), ground);
            MaoshiSetupStage3.Block("Street", new Vector3(0f, 0.005f, 35f), new Vector3(10f, 0.01f, 110f), street);

            // Дома с плоскими крышами.
            var checkpoints = new List<Transform>();
            for (int i = 0; i < Roofs.Length; i++)
            {
                var r = Roofs[i];
                float length = r.Z1 - r.Z0;
                float zMid = (r.Z0 + r.Z1) * 0.5f;
                MaoshiSetupStage3.Block($"House{i}", new Vector3(r.X, (r.Top - 0.1f) * 0.5f, zMid), new Vector3(r.Width, r.Top - 0.1f, length), walls[i % walls.Length]);
                MaoshiSetupStage3.Block($"Roof{i}", new Vector3(r.X, r.Top - 0.05f, zMid), new Vector3(r.Width + 0.2f, 0.1f, length + 0.1f), roofTar);

                var checkpoint = new GameObject($"Checkpoint{i}").transform;
                checkpoint.SetPositionAndRotation(new Vector3(r.X, r.Top + 0.3f, r.Z0 + 1.5f), Quaternion.LookRotation(Vector3.forward));
                checkpoints.Add(checkpoint);
            }

            // Доска над широким провалом между 3-й и 4-й крышей.
            var r2 = Roofs[2];
            MaoshiSetupStage3.Block("Plank", new Vector3(r2.X, r2.Top - 0.08f, (r2.Z1 + Roofs[3].Z0) * 0.5f), new Vector3(0.75f, 0.16f, Roofs[3].Z0 - r2.Z1 + 0.6f), wood);

            // Крыша 1: антенны (стойка — препятствие, перекладины выше кота).
            var r1 = Roofs[1];
            Antenna(new Vector3(r1.X - 1.5f, r1.Top, 16f), metal);
            Transform antennaPerch = Antenna(new Vector3(r1.X + 1.6f, r1.Top, 18.6f), metal);
            Antenna(new Vector3(r1.X - 0.6f, r1.Top, 21.6f), metal);

            // Крыша 2: труба посередине.
            MaoshiSetupStage3.Block("Chimney", new Vector3(r2.X, r2.Top + 0.8f, 30f), new Vector3(1.1f, 1.6f, 1.1f), chimney);

            // Крыша 3: кондиционеры и низкий барьер-труба поперёк.
            var r3 = Roofs[3];
            MaoshiSetupStage3.Block("AC1", new Vector3(r3.X - 2f, r3.Top + 0.5f, 42f), new Vector3(1.4f, 1f, 1f), metal);
            MaoshiSetupStage3.Block("AC2", new Vector3(r3.X + 1.8f, r3.Top + 0.5f, 45.5f), new Vector3(1.4f, 1f, 1f), metal);
            MaoshiSetupStage3.Block("Hurdle", new Vector3(r3.X, r3.Top + 0.2f, 47.6f), new Vector3(r3.Width, 0.4f, 0.4f), metal);

            // Крыша 4: антенна у края.
            var r4 = Roofs[4];
            Transform antennaPerch2 = Antenna(new Vector3(r4.X + 1.1f, r4.Top, 55f), metal);

            // Крыша 5: голубятня с жёрдочкой спереди.
            var r5 = Roofs[5];
            MaoshiSetupStage3.Block("Dovecote", new Vector3(r5.X, r5.Top + 0.9f, 67.6f), new Vector3(2.6f, 1.8f, 2f), wood);
            MaoshiSetupStage3.Block("DovecoteRoof", new Vector3(r5.X, r5.Top + 1.9f, 67.6f), new Vector3(3f, 0.2f, 2.4f), chimney);
            MaoshiSetupStage3.Block("DovecoteLedge", new Vector3(r5.X, r5.Top + 0.3f, 66.3f), new Vector3(1.2f, 0.6f, 0.6f), wood);

            // Насесты голубя по порядку.
            var perches = new List<Transform>
            {
                Perch("Perch0", new Vector3(0f, Roofs[0].Top + 0.15f, 10f)),
                Perch("Perch1", antennaPerch.position),
                Perch("Perch2", new Vector3(r2.X, r2.Top + 1.75f, 30f)),
                Perch("Perch3", new Vector3(r3.X + 1.8f, r3.Top + 1.15f, 45.5f)),
                Perch("Perch4", antennaPerch2.position),
                Perch("PerchFinal", new Vector3(r5.X, r5.Top + 0.75f, 66.3f)),
            };

            var pigeon = CreatePigeon(perches[0].position);

            // Коты шеренгой на первой крыше, мордой вперёд.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(0f, Roofs[0].Top, 2f), Quaternion.LookRotation(Vector3.forward));
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var spawnerSerialized = new SerializedObject(spawner);
            spawnerSerialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            spawnerSerialized.FindProperty("layout").enumValueIndex = (int)CatSpawner.SpawnLayout.Row;
            spawnerSerialized.FindProperty("lineSpacing").floatValue = 0.95f;
            spawnerSerialized.FindProperty("spawnHeight").floatValue = 0.3f;
            spawnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Правила.
            var modeGo = new GameObject("PigeonRaceMode");
            modeGo.AddComponent<NetworkObject>();
            var mode = modeGo.AddComponent<PigeonRaceMode>();
            var serialized = new SerializedObject(mode);
            serialized.FindProperty("miniGameId").stringValue = "pigeon_race";
            serialized.FindProperty("timeLimitSeconds").floatValue = 75f;
            serialized.FindProperty("endWhenOnePlayerLeft").boolValue = false;
            serialized.FindProperty("finishZ").floatValue = 63f;
            serialized.FindProperty("finishMinY").floatValue = r5.Top - 0.5f;
            serialized.FindProperty("pigeon").objectReferenceValue = pigeon;
            SetArray(serialized.FindProperty("checkpoints"), checkpoints);
            SetArray(serialized.FindProperty("perches"), perches);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SaveWithNetworkIds(scene, ScenePath);
        }

        /// <summary>Антенна: стойка с коллайдером и две перекладины без. Возвращает точку на верхней перекладине.</summary>
        private static Transform Antenna(Vector3 basePosition, Material metal)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Antenna";
            pole.transform.position = basePosition + new Vector3(0f, 1.1f, 0f);
            pole.transform.localScale = new Vector3(0.12f, 1.1f, 0.12f);
            pole.GetComponent<Renderer>().sharedMaterial = metal;

            for (int i = 0; i < 2; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "AntennaBar" + i;
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                bar.transform.position = basePosition + new Vector3(0f, 1.7f + i * 0.4f, 0f);
                bar.transform.localScale = new Vector3(1.2f - i * 0.4f, 0.05f, 0.05f);
                bar.GetComponent<Renderer>().sharedMaterial = metal;
            }

            var top = new GameObject("AntennaTop").transform;
            top.position = basePosition + new Vector3(0.3f, 2.25f, 0f);
            return top;
        }

        private static Transform Perch(string name, Vector3 position)
        {
            var perch = new GameObject(name).transform;
            perch.position = position;
            return perch;
        }

        /// <summary>Голубь из примитивов: сизое тело, зелёная шейка, клюв, крылья на шарнирах, хвост.</summary>
        private static PigeonVisual CreatePigeon(Vector3 position)
        {
            Material grey = GetOrCreateMaterial("PigeonGrey", new Color(0.55f, 0.58f, 0.66f));
            Material dark = GetOrCreateMaterial("PigeonDark", new Color(0.36f, 0.38f, 0.46f));
            Material neck = GetOrCreateMaterial("PigeonNeck", new Color(0.30f, 0.56f, 0.46f));
            Material beak = GetOrCreateMaterial("PigeonBeak", new Color(0.95f, 0.66f, 0.30f));

            var root = new GameObject("Pigeon");
            root.transform.position = position;
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            model.localScale = Vector3.one * 1.6f; // крупнее настоящего — чтобы видно издалека

            Part(PrimitiveType.Sphere, "Body", model, new Vector3(0f, 0.15f, 0f), new Vector3(0.26f, 0.24f, 0.36f), grey);
            Part(PrimitiveType.Sphere, "Neck", model, new Vector3(0f, 0.26f, 0.12f), new Vector3(0.16f, 0.16f, 0.16f), neck);
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(model, false);
            head.localPosition = new Vector3(0f, 0.34f, 0.16f);
            Part(PrimitiveType.Sphere, "Head", head, Vector3.zero, new Vector3(0.13f, 0.13f, 0.14f), dark);
            Part(PrimitiveType.Cube, "Beak", head, new Vector3(0f, -0.01f, 0.08f), new Vector3(0.03f, 0.03f, 0.06f), beak);
            Part(PrimitiveType.Cube, "Tail", model, new Vector3(0f, 0.17f, -0.22f), new Vector3(0.14f, 0.03f, 0.16f), dark);

            Transform wingLeft = Wing("WingL", model, -1f, grey);
            Transform wingRight = Wing("WingR", model, 1f, grey);

            var visual = root.AddComponent<PigeonVisual>();
            var serialized = new SerializedObject(visual);
            serialized.FindProperty("head").objectReferenceValue = head;
            serialized.FindProperty("wingLeft").objectReferenceValue = wingLeft;
            serialized.FindProperty("wingRight").objectReferenceValue = wingRight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return visual;
        }

        private static Transform Wing(string name, Transform parent, float side, Material material)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0.11f * side, 0.2f, 0f);
            Part(PrimitiveType.Cube, name + "Feathers", pivot, new Vector3(0.15f * side, 0f, -0.02f), new Vector3(0.3f, 0.025f, 0.22f), material);
            return pivot;
        }

        private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private static void SetArray(SerializedProperty property, List<Transform> items)
        {
            property.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ───────────────────────── Общее ─────────────────────────

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

            color.a = 1f;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", 0.1f);
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
