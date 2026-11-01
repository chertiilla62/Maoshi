using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.MiniGames.JanitorEscape;
using Maoshi.MiniGames.MouseHoles;
using UnityEditor.Animations;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 17: оформление сцен мини-игр. Площадки и правила не меняются: заглушки получают текстуры
    /// и модели Kenney (коллайдеры остаются прежними), вокруг ставится двор — дома, деревья, фонари, машины, небо.
    /// Запуск: Maoshi → Setup → Stage 17 - Dress Mini-Games (все сцены) или по одной сцене.
    /// После пересборки мини-игры её Stage-скриптом этот этап нужно запустить снова.
    /// </summary>
    public static class MaoshiSetupStage17
    {
        private static readonly (string scene, System.Action dress)[] Scenes =
        {
            ("LastOnFence", DressFence),
            ("MouseHoles", DressMouseHoles),
            ("LastInBox", DressBox),
            ("FlyHunt", DressFlyHunt),
            ("YarnRam", DressYarnRam),
            ("JanitorEscape", DressJanitor),
            ("PigeonRace", DressPigeonRace),
        };

        [MenuItem("Maoshi/Setup/Stage 17 - Dress Mini-Games")]
        public static void DressAll()
        {
            if (!CanRun()) return;
            foreach (var (scene, dress) in Scenes) DressScene(scene, dress);
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 17 готов: мини-игры оформлены.");
        }

        [MenuItem("Maoshi/Art/Dress Current Mini-Game")]
        public static void DressCurrent()
        {
            if (!CanRun()) return;
            string name = SceneManager.GetActiveScene().name;
            foreach (var (scene, dress) in Scenes)
            {
                if (scene != name) continue;
                DressScene(scene, dress);
                Debug.Log($"[Maoshi Art] Сцена {scene} оформлена.");
                return;
            }
            Debug.LogWarning($"[Maoshi Art] Для сцены {name} нет оформления.");
        }

        private static bool CanRun()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;
                Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                return false;
            }
            return true;
        }

        private static void DressScene(string sceneName, System.Action dress)
        {
            string path = $"{MaoshiSetup.ScenesFolder}/{sceneName}.unity";
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[Maoshi Art] Нет сцены {path}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            dress();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Как и в Stage-скриптах: пересохранить, чтобы NGO обновил ID сетевых объектов.
            var reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        // ───────────────────────── Поиск ─────────────────────────

        private static GameObject Find(string name) => GameObject.Find(name);

        private static List<GameObject> FindAll(string name)
        {
            var result = new List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) result.Add(t.gameObject);
            return result;
        }

        private static Vector2 Tile(float x, float y) => new(x, y);

        /// <summary>Невидимая стенка — чтобы с площадки не уходили (бортики ниже прыжка кота).</summary>
        private static void InvisibleWall(Transform parent, Vector3 center, Vector3 size)
        {
            var wall = new GameObject("InvisibleWall");
            wall.transform.SetParent(parent, false);
            wall.transform.position = center;
            wall.AddComponent<BoxCollider>().size = size;
        }

        private static void InvisibleRing(Transform parent, float half, float height = 3f, float y = 0f)
        {
            InvisibleWall(parent, new Vector3(0f, y + height * 0.5f, half), new Vector3(half * 2f + 1f, height, 0.3f));
            InvisibleWall(parent, new Vector3(0f, y + height * 0.5f, -half), new Vector3(half * 2f + 1f, height, 0.3f));
            InvisibleWall(parent, new Vector3(half, y + height * 0.5f, 0f), new Vector3(0.3f, height, half * 2f + 1f));
            InvisibleWall(parent, new Vector3(-half, y + height * 0.5f, 0f), new Vector3(0.3f, height, half * 2f + 1f));
        }

        private static void RetexBorders()
        {
            foreach (var name in new[] { "BorderN", "BorderS", "BorderE", "BorderW" })
                ArtKit.Retexture(Find(name), ArtLibrary.Planks, ArtLibrary.WoodDark, Tile(1f, 1f), 1f);
        }

        // ───────────────────────── Сцены ─────────────────────────

        private static void DressFence()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Dirt, ArtLibrary.Grass, Tile(4f, 4f), 4f);
            for (int i = 0; i < 14; i++)
                ArtKit.Retexture(Find($"Segment{i:00}"), ArtLibrary.Planks, ArtLibrary.WoodDark, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("BenchPlatform"), ArtLibrary.Concrete, ArtLibrary.Sidewalk, Tile(2f, 2f), 2f);

            var root = ArtLibrary.Surround(new Vector2(-15f, -15f), new Vector2(15f, 15f), 6f, 11);
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(8.6f, 0.2f, -1.6f), -90f, new Vector3(1.9f, 1.3f, 1.9f), root);
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(8.6f, 0.2f, 1.6f), -90f, new Vector3(1.9f, 1.3f, 1.9f), root);

            // Забор продолжается за край площадки (просто вид), по сторонам — огород и двор.
            for (float z = -15f; z < 15f; z += 1f)
            {
                if (Mathf.Abs(z) < 7.5f) continue;
                ArtKit.Box("FenceDecor", new Vector3(0f, 0.9f, z + 0.5f), new Vector3(0.12f, 1.8f, 0.96f), ArtLibrary.Planks, root, false);
            }
            var rng = new System.Random(2);
            for (int i = 0; i < 18; i++)
            {
                Vector3 p = new Vector3(-3f - (float)rng.NextDouble() * 10f, 0f, -13f + (float)rng.NextDouble() * 26f);
                if (Mathf.Abs(p.z) < 1f && p.x > -4f) continue;
                ArtKit.PlaceFit(i % 3 == 0 ? "Nature/plant_bushDetailed" : "Nature/grass_large", p, rng.Next(360), new Vector3(1.2f, 0.8f, 1.2f), root);
            }
            ArtLibrary.Tree(root, "Nature/tree_oak", new Vector3(-11f, 0f, -9f), 7f);
            ArtLibrary.Tree(root, "Nature/tree_default", new Vector3(-12f, 0f, 8f), 6.5f);
            ArtKit.PlaceFit("Cars/sedan", new Vector3(11.5f, 0f, -9f), 0f, new Vector3(2f, 2f, 4.3f), root, true);
            ArtLibrary.Lighting(warm: true);
        }

        private static void DressMouseHoles()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Dirt, ArtLibrary.Dirt, Tile(3f, 3f), 3f);
            RetexBorders();
            var root = ArtLibrary.Surround(new Vector2(-9f, -9f), new Vector2(9f, 9f), 7f, 21);
            InvisibleRing(root, 9f);

            // Сараи и хлам вокруг — двор у сараев, где живут мыши.
            for (int i = 0; i < 4; i++)
            {
                var shed = ArtKit.UvBox("Shed", new Vector3(-7.5f + i * 5f, 0f, 17.5f), new Vector3(4.6f, 2.6f, 3.6f), ArtLibrary.ShedPlanks, ArtLibrary.RoofSheet, Tile(1f, 1f), 2f, root);
                ArtKit.Box("ShedDoor", new Vector3(-7.5f + i * 5f, 1.0f, 15.68f), new Vector3(1.0f, 2.0f, 0.05f), ArtLibrary.WoodDark, root, false);
                _ = shed;
            }
            ArtKit.PlaceFit("Nature/log", new Vector3(-12f, 0f, -4f), 90f, new Vector3(2.2f, 0.6f, 0.8f), root, true);
            ArtKit.PlaceFit("Nature/log", new Vector3(-12f, 0.5f, -4f), 80f, new Vector3(2.2f, 0.6f, 0.8f), root);
            ArtKit.PlaceFit("Platformer/barrel", new Vector3(12f, 0f, 3f), 0f, new Vector3(0.8f, 1f, 0.8f), root, true);
            ArtKit.PlaceFit("Platformer/crate", new Vector3(12f, 0f, -3f), 20f, new Vector3(0.9f, 0.9f, 0.9f), root, true);
            ArtKit.PlaceFit("Graveyard/trunk", new Vector3(11.5f, 0f, -6f), 0f, new Vector3(1.0f, 0.8f, 1.0f), root, true);

            // Мышки покрасивее: круглое тело, большие уши, глазки, хвостик. Цвет золотой мыши красит MouseHole.
            var mouseMat = ArtKit.Color("MouseGrey", new Color(0.62f, 0.60f, 0.62f));
            var pink = ArtKit.Color("MousePink", new Color(0.95f, 0.62f, 0.66f));
            var black = ArtKit.Color("MouseEye", new Color(0.06f, 0.06f, 0.07f), 0.8f);
            var dirtDark = ArtKit.Color("HoleRim", new Color(0.36f, 0.27f, 0.19f));
            foreach (var hole in Object.FindObjectsByType<MouseHole>(FindObjectsSortMode.None))
            {
                var mouse = hole.transform.Find("Mouse");
                if (mouse == null) continue;
                for (int i = mouse.childCount - 1; i >= 0; i--) Object.DestroyImmediate(mouse.GetChild(i).gameObject);

                var body = Part(mouse, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.11f, -0.02f), new Vector3(0.26f, 0.22f, 0.36f), mouseMat);
                var head = Part(mouse, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.15f, 0.19f), new Vector3(0.19f, 0.18f, 0.22f), mouseMat);
                var earL = Part(mouse, PrimitiveType.Cylinder, "EarL", new Vector3(-0.085f, 0.27f, 0.15f), new Vector3(0.13f, 0.008f, 0.13f), mouseMat, new Vector3(80f, 0f, 20f));
                var earR = Part(mouse, PrimitiveType.Cylinder, "EarR", new Vector3(0.085f, 0.27f, 0.15f), new Vector3(0.13f, 0.008f, 0.13f), mouseMat, new Vector3(80f, 0f, -20f));
                Part(mouse, PrimitiveType.Cylinder, "EarInL", new Vector3(-0.085f, 0.27f, 0.157f), new Vector3(0.09f, 0.006f, 0.09f), pink, new Vector3(80f, 0f, 20f));
                Part(mouse, PrimitiveType.Cylinder, "EarInR", new Vector3(0.085f, 0.27f, 0.157f), new Vector3(0.09f, 0.006f, 0.09f), pink, new Vector3(80f, 0f, -20f));
                Part(mouse, PrimitiveType.Sphere, "EyeL", new Vector3(-0.05f, 0.18f, 0.28f), new Vector3(0.035f, 0.04f, 0.035f), black);
                Part(mouse, PrimitiveType.Sphere, "EyeR", new Vector3(0.05f, 0.18f, 0.28f), new Vector3(0.035f, 0.04f, 0.035f), black);
                Part(mouse, PrimitiveType.Sphere, "Nose", new Vector3(0f, 0.14f, 0.31f), new Vector3(0.04f, 0.035f, 0.04f), pink);
                Part(mouse, PrimitiveType.Cylinder, "Tail", new Vector3(0f, 0.06f, -0.34f), new Vector3(0.025f, 0.16f, 0.025f), pink, new Vector3(-75f, 0f, 0f));

                var so = new SerializedObject(hole);
                var renderers = so.FindProperty("mouseRenderers");
                var tinted = new[] { body, head, earL, earR };
                renderers.arraySize = tinted.Length;
                for (int i = 0; i < tinted.Length; i++) renderers.GetArrayElementAtIndex(i).objectReferenceValue = tinted[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                // Холмик земли вокруг норки.
                var oldRim = hole.transform.Find("Rim");
                if (oldRim != null) Object.DestroyImmediate(oldRim.gameObject);
                var rim = Part(hole.transform, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.008f, 0f), new Vector3(1.15f, 0.008f, 1.15f), dirtDark);
                rim.gameObject.isStatic = true;
            }

            ArtLibrary.Lighting();
        }

        private static void DressBox()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Concrete, ArtLibrary.Asphalt, Tile(4f, 4f), 5f);
            ArtKit.Retexture(Find("BenchIsland"), ArtLibrary.Concrete, ArtLibrary.Sidewalk, Tile(2f, 2f), 2f);

            // Коробка: картон (размер меняется в игре, поэтому без переразвёртки — только материал).
            var cardboard = ArtLibrary.Cardboard;
            var cardboardDark = ArtKit.Textured("CardboardDarkTex", $"{ArtKit.ArtFolder}/Textures/Cardboard.png");
            cardboardDark.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.64f));
            EditorUtility.SetDirty(cardboardDark);
            foreach (var name in new[] { "WallNorth", "WallSouth", "WallEast", "WallWest" })
            {
                var wall = Find(name);
                if (wall != null) wall.GetComponent<Renderer>().sharedMaterial = cardboard;
            }
            var floor = Find("Floor");
            if (floor != null) floor.GetComponent<Renderer>().sharedMaterial = cardboardDark;

            var root = ArtLibrary.Surround(new Vector2(-11f, -11f), new Vector2(21.5f, 11f), 7f, 31);
            InvisibleRing(root, 11f);
            InvisibleWall(root, new Vector3(18.5f, 1.5f, 5f), new Vector3(6.4f, 3f, 0.3f));
            InvisibleWall(root, new Vector3(18.5f, 1.5f, -5f), new Vector3(6.4f, 3f, 0.3f));
            InvisibleWall(root, new Vector3(21.6f, 1.5f, 0f), new Vector3(0.3f, 3f, 10.4f));
            InvisibleWall(root, new Vector3(15.4f, 1.5f, 0f), new Vector3(0.3f, 3f, 10.4f));
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(20.6f, 0f, -2f), -90f, new Vector3(1.9f, 1.3f, 1.9f), root);
            ArtKit.PlaceFit("Graveyard/bench", new Vector3(20.6f, 0f, 2f), -90f, new Vector3(1.9f, 1.3f, 1.9f), root);
            // Пустые коробки валяются вокруг — откуда-то же взялась большая.
            ArtKit.PlaceFit("Furniture/cardboardBoxOpen", new Vector3(-13.5f, 0f, 6f), 30f, new Vector3(1.2f, 0.9f, 1.2f), root, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(-13.5f, 0f, 4.4f), -10f, new Vector3(0.8f, 0.8f, 0.8f), root, true);
            ArtKit.PlaceFit("Furniture/cardboardBoxClosed", new Vector3(-13.3f, 0.8f, 4.5f), 25f, new Vector3(0.6f, 0.6f, 0.6f), root);
            ArtLibrary.Lighting();
        }

        private static void DressFlyHunt()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Concrete, ArtLibrary.Asphalt, Tile(4f, 4f), 5f);
            foreach (var name in new[] { "BorderN", "BorderS", "BorderE", "BorderW" })
                ArtKit.Retexture(Find(name), ArtLibrary.BrickPlain, ArtLibrary.Concrete, Tile(1.5f, 1.5f), 1.5f);
            for (int i = 0; i < 5; i++) ArtKit.Dress(Find($"Crate{i}"), i == 4 ? "Platformer/crateStrong" : "Platformer/crate");

            var root = ArtLibrary.Surround(new Vector2(-8.5f, -8.5f), new Vector2(8.5f, 8.5f), 7f, 41);
            InvisibleRing(root, 8.5f);

            // Помойка за бортиком: мух тут хватает.
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new Vector3(-3f + i * 2.4f, 0f, 11.2f);
                ArtKit.Box("Dumpster", p + Vector3.up * 0.65f, new Vector3(2f, 1.1f, 1.3f), ArtLibrary.Dumpster, root);
                var lid = ArtKit.Box("Lid", p + new Vector3(0f, 1.25f, 0.05f), new Vector3(2.06f, 0.08f, 1.4f), ArtKit.Color("DumpsterLid", new Color(0.16f, 0.32f, 0.22f), 0.3f), root, false);
                lid.transform.rotation = Quaternion.Euler(i == 1 ? 20f : 5f, 0f, 0f);
            }
            ArtKit.PlaceFit("Food/fishBones", new Vector3(10.5f, 0f, -2f), 20f, new Vector3(0.6f, 0.2f, 0.6f), root);
            ArtKit.PlaceFit("Furniture/trashcan", new Vector3(-10.5f, 0f, 4f), 0f, new Vector3(0.6f, 0.9f, 0.6f), root, true);
            ArtLibrary.Lighting();
        }

        private static void DressYarnRam()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Concrete, ArtLibrary.Asphalt, Tile(4f, 4f), 6f);
            ArtKit.Retexture(Find("Building"), ArtLibrary.FacadePanel, ArtLibrary.RoofTar, Tile(6f, ArtLibrary.Floor * 2f), 4f);
            ArtKit.Retexture(Find("Roof"), ArtLibrary.RoofTar, ArtLibrary.RoofTar, Tile(4f, 4f), 4f);
            ArtKit.Retexture(Find("Chimney"), ArtLibrary.BrickPlain, ArtLibrary.Concrete, Tile(1.5f, 1.5f), 1.5f);
            ArtKit.Retexture(Find("Vent"), ArtLibrary.MetalSheet, ArtLibrary.MetalSheet, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("Skylight"), ArtLibrary.MetalSheet, ArtLibrary.GlassDark, Tile(1f, 1f), 2f);

            // Соседние крыши разной высоты (не допрыгнуть) и двор внизу.
            var root = ArtLibrary.Surround(new Vector2(-7f, -7f), new Vector2(7f, 7f), 5f, 61, 0f, false);
            ArtLibrary.House(root, new Vector3(-20f, 0f, 0f), new Vector3(12f, ArtLibrary.Floor * 5f, 26f), ArtLibrary.FacadeBrick);
            ArtLibrary.House(root, new Vector3(20f, 0f, 3f), new Vector3(12f, ArtLibrary.Floor * 2f, 22f), ArtLibrary.FacadeBlue);
            ArtLibrary.House(root, new Vector3(0f, 0f, 22f), new Vector3(34f, ArtLibrary.Floor * 8f, 12f), ArtLibrary.FacadePanel);
            ArtLibrary.House(root, new Vector3(4f, 0f, -24f), new Vector3(30f, ArtLibrary.Floor * 4f, 12f), ArtLibrary.FacadeBrick);
            foreach (var p in new[] { new Vector3(-20f, ArtLibrary.Floor * 5f, -6f), new Vector3(0f, ArtLibrary.Floor * 8f, 24f), new Vector3(8f, ArtLibrary.Floor * 8f, 20f) })
                ArtKit.PlaceFit("Furniture/televisionAntenna", p, 30f, new Vector3(2.4f, 1.4f, 1.4f), root);
            ArtLibrary.Lighting(warm: true, 50f, 180f);
        }

        private static void DressJanitor()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Concrete, ArtLibrary.Grass, Tile(4f, 4f), 4f);
            ArtKit.Retexture(Find("Asphalt"), ArtLibrary.Asphalt, ArtLibrary.Asphalt, Tile(6f, 6f), 6f);
            ArtKit.Retexture(Find("HouseWall"), ArtLibrary.FacadePanel, ArtLibrary.RoofTar, Tile(6f, ArtLibrary.Floor * 2f), 4f);
            foreach (var name in new[] { "FenceFront", "FenceLeft", "FenceRight" })
                ArtKit.Retexture(Find(name), ArtLibrary.Planks, ArtLibrary.WoodDark, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("Shed"), ArtLibrary.ShedPlanks, ArtLibrary.ShedPlanks, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("ShedRoof"), ArtLibrary.RoofSheet, ArtLibrary.RoofSheet, Tile(2f, 2f), 2f);
            var door = Find("ShedDoor");
            if (door != null) door.GetComponent<Renderer>().sharedMaterial = ArtLibrary.WoodDark;

            // Машина: модель по габаритам заглушки, кабина-заглушка прячется.
            ArtKit.Dress(Find("Car"), "Cars/sedan", uniform: true, yaw: 90f, extraScale: 1.35f);
            ArtKit.Hide(Find("CarCabin"));
            var cabin = Find("CarCabin");
            if (cabin != null) cabin.transform.localScale = new Vector3(cabin.transform.localScale.x, cabin.transform.localScale.y * 0.6f, cabin.transform.localScale.z);

            // Мусорные баки — зелёные контейнеры с крышками.
            foreach (var name in new[] { "Bin1", "Bin2", "Bin3" })
            {
                var bin = Find(name);
                if (bin == null) continue;
                bin.GetComponent<Renderer>().sharedMaterial = ArtLibrary.Dumpster;
                var oldLid = bin.transform.Find("Lid");
                if (oldLid != null) Object.DestroyImmediate(oldLid.gameObject);
                var b = bin.GetComponent<Renderer>().bounds;
                var lid = ArtKit.Box("Lid", new Vector3(b.center.x, b.max.y + 0.04f, b.center.z), new Vector3(b.size.x + 0.06f, 0.08f, b.size.z + 0.1f),
                    ArtKit.Color("DumpsterLid", new Color(0.16f, 0.32f, 0.22f), 0.3f), bin.transform, false);
                lid.transform.SetParent(bin.transform, true);
            }

            // Лавочки — настоящие (коллайдер заглушки остаётся).
            ArtKit.Dress(Find("Bench1"), "Graveyard/bench");
            ArtKit.Dress(Find("Bench2"), "Graveyard/bench", yaw: 180f);
            ArtKit.Dress(Find("Bench3"), "Graveyard/bench", yaw: 90f);

            ArtKit.Retexture(Find("Sandbox"), ArtLibrary.Sand, ArtLibrary.Sand, Tile(2f, 2f), 2f);
            foreach (var name in new[] { "SandboxN", "SandboxS", "SandboxW", "SandboxE" })
                ArtKit.Retexture(Find(name), ArtLibrary.Planks, ArtLibrary.Wood, Tile(1f, 1f), 1f);
            var bed = Find("Flowerbed");
            ArtKit.Retexture(bed, ArtLibrary.BrickPlain, ArtLibrary.Grass, Tile(1.5f, 1.5f), 2f);

            var root = ArtLibrary.Surround(new Vector2(-17f, -13f), new Vector2(17f, 13f), 5f, 71, 0f, false);
            if (bed != null)
            {
                var b = bed.GetComponent<Renderer>().bounds;
                string[] flowers = { "Nature/flower_redA", "Nature/flower_yellowA", "Nature/flower_purpleA", "Nature/flower_redB" };
                var rng = new System.Random(5);
                for (int i = 0; i < 14; i++)
                {
                    Vector3 p = new Vector3(b.min.x + 0.3f + (float)rng.NextDouble() * (b.size.x - 0.6f), b.max.y, b.min.z + 0.3f + (float)rng.NextDouble() * (b.size.z - 0.6f));
                    ArtKit.PlaceFit(flowers[i % flowers.Length], p, rng.Next(360), new Vector3(0.5f, 0.45f, 0.5f), root);
                }
            }

            // Деревья: модели вместо цилиндров (коллайдер-ствол остаётся).
            string[] trees = { "Nature/tree_oak", "Nature/tree_default", "Nature/tree_detailed", "Nature/tree_fat" };
            int t = 0;
            foreach (var trunk in FindAll("TreeTrunk"))
            {
                ArtKit.Hide(trunk);
                Vector3 basePoint = trunk.GetComponent<Collider>().bounds.min;
                basePoint = new Vector3(trunk.transform.position.x, 0f, trunk.transform.position.z);
                ArtLibrary.Tree(root, trees[t++ % trees.Length], basePoint, 6.5f, false);
            }

            // Дом над стеной двора — пятиэтажка целиком, и соседи за заборами.
            var wall = Find("HouseWall");
            if (wall != null)
            {
                var b = wall.GetComponent<Collider>().bounds;
                ArtKit.UvBox("HouseBody", new Vector3(b.center.x, 0f, b.max.z + 6f), new Vector3(b.size.x, ArtLibrary.Floor * 5f, 12f), ArtLibrary.FacadePanel, ArtLibrary.RoofTar,
                    new Vector2(6f, ArtLibrary.Floor * 2f), 4f, root);
            }
            foreach (var janitor in Object.FindObjectsByType<Janitor>(FindObjectsSortMode.None))
                JanitorCharacter(janitor);

            ArtLibrary.House(root, new Vector3(-31f, 0f, 0f), new Vector3(12f, ArtLibrary.Floor * 5f, 40f), ArtLibrary.FacadeBrick);
            ArtLibrary.House(root, new Vector3(31f, 0f, 0f), new Vector3(12f, ArtLibrary.Floor * 9f, 40f), ArtLibrary.FacadeBlue);
            ArtLibrary.House(root, new Vector3(0f, 0f, -27f), new Vector3(60f, ArtLibrary.Floor * 5f, 12f), ArtLibrary.FacadePanel);
            ArtLibrary.Lighting();
        }

        private static void DressPigeonRace()
        {
            ArtKit.Retexture(Find("Ground"), ArtLibrary.Concrete, ArtLibrary.Asphalt, Tile(4f, 4f), 6f);
            ArtKit.Retexture(Find("Street"), ArtLibrary.Road, ArtLibrary.Road, Tile(6f, 6f), 6f);
            Material[] facades = { ArtLibrary.FacadePanel, ArtLibrary.FacadeBrick, ArtLibrary.FacadeBlue, ArtLibrary.FacadeBrick, ArtLibrary.FacadePanel, ArtLibrary.FacadeBlue };
            for (int i = 0; i < 6; i++)
            {
                ArtKit.Retexture(Find($"House{i}"), facades[i], ArtLibrary.RoofTar, Tile(6f, ArtLibrary.Floor * 2f), 4f);
                ArtKit.Retexture(Find($"Roof{i}"), ArtLibrary.RoofTar, ArtLibrary.RoofTar, Tile(4f, 4f), 4f);
            }
            ArtKit.Retexture(Find("Plank"), ArtLibrary.Planks, ArtLibrary.Planks, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("Chimney"), ArtLibrary.BrickPlain, ArtLibrary.Concrete, Tile(1.5f, 1.5f), 1.5f);
            ArtKit.Retexture(Find("AC1"), ArtLibrary.AcUnit, ArtLibrary.MetalSheet, Tile(1.4f, 1f), 1f);
            ArtKit.Retexture(Find("AC2"), ArtLibrary.AcUnit, ArtLibrary.MetalSheet, Tile(1.4f, 1f), 1f);
            var hurdle = Find("Hurdle");
            if (hurdle != null) hurdle.GetComponent<Renderer>().sharedMaterial = ArtLibrary.MetalDark;
            ArtKit.Retexture(Find("Dovecote"), ArtLibrary.ShedPlanks, ArtLibrary.ShedPlanks, Tile(1f, 1f), 1f);
            ArtKit.Retexture(Find("DovecoteRoof"), ArtLibrary.RoofSheet, ArtLibrary.RoofSheet, Tile(2f, 2f), 2f);
            var ledge = Find("DovecoteLedge");
            if (ledge != null) ledge.GetComponent<Renderer>().sharedMaterial = ArtLibrary.Wood;
            foreach (var antenna in FindAll("Antenna")) antenna.GetComponent<Renderer>().sharedMaterial = ArtLibrary.MetalDark;
            foreach (var bar in FindAll("AntennaBar0")) bar.GetComponent<Renderer>().sharedMaterial = ArtLibrary.MetalDark;
            foreach (var bar in FindAll("AntennaBar1")) bar.GetComponent<Renderer>().sharedMaterial = ArtLibrary.MetalDark;

            // Улица: дома через дорогу, деревья, машины, фонари.
            var root = ArtLibrary.Surround(new Vector2(-6f, -4f), new Vector2(6f, 72f), 10f, 81, 0f, false);
            ArtLibrary.House(root, new Vector3(-21f, 0f, 35f), new Vector3(12f, ArtLibrary.Floor * 6f, 80f), ArtLibrary.FacadePanel);
            ArtLibrary.House(root, new Vector3(21f, 0f, 20f), new Vector3(12f, ArtLibrary.Floor * 4f, 40f), ArtLibrary.FacadeBrick);
            ArtLibrary.House(root, new Vector3(23f, 0f, 58f), new Vector3(12f, ArtLibrary.Floor * 9f, 30f), ArtLibrary.FacadeBlue);
            ArtLibrary.House(root, new Vector3(0f, 0f, 88f), new Vector3(50f, ArtLibrary.Floor * 7f, 12f), ArtLibrary.FacadeBrick);
            ArtLibrary.Lighting(fogStart: 50f, fogEnd: 190f);
        }

        // ───────────────────────── Дворник ─────────────────────────

        private const string JanitorController = ArtKit.ArtFolder + "/Animators/AC_Janitor.controller";
        private const float JanitorHeight = 1.85f;

        /// <summary>
        /// Дворник-персонаж Kenney в оранжевом жилете (скин janitor.png) с анимациями idle/run.
        /// Примитивное тело прячется, метла (BroomPivot) остаётся и машет как раньше.
        /// </summary>
        private static void JanitorCharacter(Janitor janitor)
        {
            var body = janitor.transform.Find("Body");
            if (body == null) return;

            var old = body.Find("Character");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (Transform child in body)
            {
                if (child.name == "BroomPivot") continue;
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyImportSettings.CharacterModel);
            if (model == null) return;
            var character = (GameObject)PrefabUtility.InstantiatePrefab(model);
            character.name = "Character";
            character.transform.SetParent(body, false);
            character.transform.localPosition = Vector3.zero;
            character.transform.localRotation = Quaternion.identity;
            character.transform.localScale = Vector3.one;
            var natural = ArtKit.RendererBounds(character);
            float scale = JanitorHeight / Mathf.Max(0.01f, natural.size.y);
            character.transform.localScale = Vector3.one * scale;
            var placed = ArtKit.RendererBounds(character);
            character.transform.position += Vector3.up * (janitor.transform.position.y - placed.min.y);

            var skin = ArtKit.Textured("JanitorSkin", KenneyImportSettings.Root + "/Characters/Skins/janitor.png");
            foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = skin;
                r.sharedMaterials = materials;
            }

            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            animator.runtimeAnimatorController = GetJanitorController();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Метла — двумя руками перед грудью; руки к черенку тянет JanitorRig (IK).
            var broom = body.Find("BroomPivot");
            if (broom != null) broom.localPosition = new Vector3(0.16f, JanitorHeight * 0.62f, 0.3f);

            var rig = janitor.GetComponent<JanitorRig>();
            if (rig == null) rig = janitor.gameObject.AddComponent<JanitorRig>();
            Transform Bone(string name)
            {
                foreach (var t in character.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
                Debug.LogWarning("[Maoshi Art] Нет кости " + name);
                return null;
            }
            var rigSo = new SerializedObject(rig);
            rigSo.FindProperty("janitor").objectReferenceValue = janitor;
            rigSo.FindProperty("broom").objectReferenceValue = broom;
            rigSo.FindProperty("spine").objectReferenceValue = Bone("Spine");
            rigSo.FindProperty("chest").objectReferenceValue = Bone("UpperChest");
            rigSo.FindProperty("upperArmR").objectReferenceValue = Bone("RightArm");
            rigSo.FindProperty("foreArmR").objectReferenceValue = Bone("RightForeArm");
            rigSo.FindProperty("handR").objectReferenceValue = Bone("RightHand");
            rigSo.FindProperty("upperArmL").objectReferenceValue = Bone("LeftArm");
            rigSo.FindProperty("foreArmL").objectReferenceValue = Bone("LeftForeArm");
            rigSo.FindProperty("handL").objectReferenceValue = Bone("LeftHand");
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(janitor);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AnimatorController GetJanitorController()
        {
            MaoshiSetup.EnsureFolder(ArtKit.ArtFolder + "/Animators");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(JanitorController);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(JanitorController);

            AnimationClip Clip(string file)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{KenneyImportSettings.Root}/Characters/Animations/{file}.fbx"))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
                return null;
            }

            var layer = controller.layers[0];
            var machine = layer.stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            if (System.Array.TrueForAll(controller.parameters, p => p.name != "Speed"))
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            var idle = Clip("idle");
            var run = Clip("run");
            if (idle != null) tree.AddChild(idle, 0f);
            if (run != null) tree.AddChild(run, 4f);
            machine.defaultState = locomotion;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // ───────────────────────── Общее ─────────────────────────

        private static Renderer Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Material material, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
