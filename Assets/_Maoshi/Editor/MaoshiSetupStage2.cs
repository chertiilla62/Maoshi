using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 2: префаб кота-заглушки из примитивов и тестовая площадка в сцене Hub.
    /// Запуск: Maoshi → Setup → Stage 2 - Cat Controller. Hub пересоздаётся (пока это тестовая площадка).
    /// </summary>
    public static class MaoshiSetupStage2
    {
        private const string MaterialsFolder = MaoshiSetup.Root + "/Materials";
        private const string PhysicsFolder = MaoshiSetup.Root + "/Physics";
        public const string CatPrefabPath = MaoshiSetup.PrefabsFolder + "/Cat.prefab";

        [MenuItem("Maoshi/Setup/Stage 2 - Cat Controller")]
        public static void SetupStage2()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            MaoshiSetup.EnsureFolder(MaterialsFolder);
            MaoshiSetup.EnsureFolder(PhysicsFolder);
            MaoshiSetup.EnsureFolder(MaoshiSetup.PrefabsFolder);

            var materials = CreateMaterials();
            var catPhysics = CreateCatPhysicsMaterial();

            GameObject catPrefab = CreateCatPrefab(materials, catPhysics);
            MaoshiSetup.GetOrCreatePrefabsList(catPrefab);

            CreatePlayground(materials, catPrefab);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 2 готов: префаб Cat и тестовая площадка в Hub.");
        }

        // ───────────────────────── Материалы ─────────────────────────

        private static Dictionary<string, Material> CreateMaterials()
        {
            var colors = new Dictionary<string, Color>
            {
                { "CatFur", Color.white }, // белый, чтобы цвет игрока ложился без искажений
                { "CatEyes", new Color(0.08f, 0.08f, 0.1f) },
                { "CatNose", new Color(0.95f, 0.55f, 0.6f) },
                { "Ground", new Color(0.55f, 0.53f, 0.48f) },  // пыльный асфальт двора
                { "Wood", new Color(0.62f, 0.42f, 0.25f) },
                { "Fence", new Color(0.40f, 0.28f, 0.20f) },
                { "Brick", new Color(0.70f, 0.36f, 0.28f) },
                { "Metal", new Color(0.45f, 0.55f, 0.62f) }
            };

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");

            var result = new Dictionary<string, Material>();
            foreach (var pair in colors)
            {
                string path = $"{MaterialsFolder}/M_{pair.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(lit);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.SetColor("_BaseColor", pair.Value);
                material.SetColor("_Color", pair.Value);
                material.SetFloat("_Smoothness", 0.15f);
                EditorUtility.SetDirty(material);
                result[pair.Key] = material;
            }

            return result;
        }

        private static PhysicsMaterial CreateCatPhysicsMaterial()
        {
            string path = PhysicsFolder + "/PM_Cat.physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial("Cat");
                AssetDatabase.CreateAsset(material, path);
            }

            // Низкое трение: кот не «прилипает» к стенам, но и не катается по полу как шайба.
            material.dynamicFriction = 0.25f;
            material.staticFriction = 0.25f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounciness = 0f;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ───────────────────────── Кот ─────────────────────────

        private static GameObject CreateCatPrefab(Dictionary<string, Material> m, PhysicsMaterial catPhysics)
        {
            var root = new GameObject("Cat");

            // Физика: горизонтальная капсула по длине тела. Опорная точка — между лап на земле.
            var body = root.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.angularDamping = 0.5f;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2; // ось Z
            capsule.radius = 0.32f;
            capsule.height = 1.25f;
            capsule.center = new Vector3(0f, 0.32f, 0.05f);
            capsule.sharedMaterial = catPhysics;

            // Сеть: позицию рассылает владелец, у остальных кот кинематический.
            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            var netBody = root.AddComponent<NetworkRigidbody>();
            netBody.UseRigidBodyForMotion = true;

            root.AddComponent<CatController>();
            var appearance = root.AddComponent<CatAppearance>();

            // Модель-заглушка из кубиков (lowpoly-кот). Мордой по +Z.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            var fur = new List<Renderer>
            {
                Part(model, "Body", new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.38f, 0.8f), Vector3.zero, m["CatFur"]),
                Part(model, "Head", new Vector3(0f, 0.72f, 0.46f), new Vector3(0.42f, 0.38f, 0.38f), Vector3.zero, m["CatFur"]),
                Part(model, "EarL", new Vector3(-0.13f, 0.95f, 0.44f), new Vector3(0.12f, 0.16f, 0.06f), new Vector3(0f, 0f, 20f), m["CatFur"]),
                Part(model, "EarR", new Vector3(0.13f, 0.95f, 0.44f), new Vector3(0.12f, 0.16f, 0.06f), new Vector3(0f, 0f, -20f), m["CatFur"]),
                Part(model, "Tail", new Vector3(0f, 0.65f, -0.55f), new Vector3(0.08f, 0.08f, 0.55f), new Vector3(-40f, 0f, 0f), m["CatFur"]),
                Part(model, "LegFL", new Vector3(-0.16f, 0.125f, 0.28f), new Vector3(0.12f, 0.25f, 0.12f), Vector3.zero, m["CatFur"]),
                Part(model, "LegFR", new Vector3(0.16f, 0.125f, 0.28f), new Vector3(0.12f, 0.25f, 0.12f), Vector3.zero, m["CatFur"]),
                Part(model, "LegBL", new Vector3(-0.16f, 0.125f, -0.28f), new Vector3(0.12f, 0.25f, 0.12f), Vector3.zero, m["CatFur"]),
                Part(model, "LegBR", new Vector3(0.16f, 0.125f, -0.28f), new Vector3(0.12f, 0.25f, 0.12f), Vector3.zero, m["CatFur"])
            };
            Part(model, "EyeL", new Vector3(-0.1f, 0.77f, 0.655f), new Vector3(0.07f, 0.09f, 0.02f), Vector3.zero, m["CatEyes"]);
            Part(model, "EyeR", new Vector3(0.1f, 0.77f, 0.655f), new Vector3(0.07f, 0.09f, 0.02f), Vector3.zero, m["CatEyes"]);
            Part(model, "Nose", new Vector3(0f, 0.68f, 0.66f), new Vector3(0.06f, 0.04f, 0.02f), Vector3.zero, m["CatNose"]);

            // Табличка с именем.
            var plateGo = new GameObject("Nameplate");
            plateGo.transform.SetParent(root.transform, false);
            plateGo.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            var text = plateGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = "Кот";
            plateGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            // Кубики — временная модель; настоящую модель AnimX и породы ставит Stage 8.
            var serialized = new SerializedObject(appearance);
            serialized.FindProperty("model").objectReferenceValue = model.transform;
            serialized.FindProperty("nameplate").objectReferenceValue = text;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CatPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Кубик-деталь модели без коллайдера (физика — только у капсулы).</summary>
        private static Renderer Part(GameObject parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localEulerAngles = euler;

            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        // ───────────────────────── Площадка ─────────────────────────

        private static void CreatePlayground(Dictionary<string, Material> m, GameObject catPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 8f, -12f), Quaternion.Euler(30f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.55f, 0.70f, 0.85f);
            }

            // Земля. Края открыты: упал — вернёшься на старт (проверка возврата).
            Static("Ground", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 24f), Vector3.zero, m["Ground"]);

            // Рампа и ступени разной высоты — проверка прыжка.
            Static("Ramp", PrimitiveType.Cube, new Vector3(7f, 0.95f, 5f), new Vector3(3f, 0.3f, 6f), new Vector3(-18f, 0f, 0f), m["Metal"]);
            Static("Step1", PrimitiveType.Cube, new Vector3(5f, 0.4f, -6f), new Vector3(2f, 0.8f, 2f), Vector3.zero, m["Brick"]);
            Static("Step2", PrimitiveType.Cube, new Vector3(7.5f, 0.8f, -6f), new Vector3(2f, 1.6f, 2f), Vector3.zero, m["Brick"]);
            Static("Step3", PrimitiveType.Cube, new Vector3(10f, 1.2f, -6f), new Vector3(2f, 2.4f, 2f), Vector3.zero, m["Brick"]);

            // Узкий забор — заготовка для «Последнего на заборе».
            Static("Fence", PrimitiveType.Cube, new Vector3(-8f, 0.6f, 0f), new Vector3(0.45f, 1.2f, 12f), Vector3.zero, m["Fence"]);

            // Ящики и бочки — их можно толкать и ронять.
            for (int i = 0; i < 5; i++)
                Prop($"Crate{i}", PrimitiveType.Cube, new Vector3(-3f + i * 1.5f, 0.35f, 7f), new Vector3(0.7f, 0.7f, 0.7f), 0.6f, m["Wood"]);
            for (int i = 0; i < 3; i++)
                Prop($"CrateTower{i}", PrimitiveType.Cube, new Vector3(-4f, 0.35f + i * 0.71f, -6f), new Vector3(0.7f, 0.7f, 0.7f), 0.6f, m["Wood"]);
            for (int i = 0; i < 3; i++)
                Prop($"Barrel{i}", PrimitiveType.Cylinder, new Vector3(2f + i * 1.2f, 0.4f, -3f), new Vector3(0.5f, 0.4f, 0.5f), 0.9f, m["Metal"]);

            // Спавнер котов в центре.
            var spawnerGo = new GameObject("CatSpawner");
            spawnerGo.AddComponent<NetworkObject>();
            var spawner = spawnerGo.AddComponent<CatSpawner>();
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("catPrefab").objectReferenceValue = catPrefab.GetComponent<NetworkObject>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, MaoshiSetup.HubScenePath);

            // Повторное открытие и сохранение: NGO генерирует ID сетевых объектов сцены при загрузке.
            var reopened = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        private static GameObject Static(string name, PrimitiveType type, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.isStatic = true;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Физический предмет. Принадлежит серверу, у клиентов кинематический.</summary>
        private static void Prop(string name, PrimitiveType type, Vector3 position, Vector3 scale, float mass, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            go.AddComponent<NetworkObject>();
            var netTransform = go.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            var netBody = go.AddComponent<NetworkRigidbody>();
            netBody.UseRigidBodyForMotion = true;
        }
    }
}
