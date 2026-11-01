using System.Collections.Generic;
using Maoshi.EditorTools.Art;
using Maoshi.Hub;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 16: настоящий двор в сцене Hub.
    /// Панельная пятиэтажка с тремя подъездами (север), кирпичная «хрущёвка» с чердаком (запад), ряд гаражей (восток),
    /// чугунный забор и улица с машинами (юг). Внутри: помойка, песочница, детская площадка, голубятня, бельё,
    /// доминошный стол, деревья, кусты, фонари, машины. У каждой из 10 зон — флажок в цвет владельца (ZoneMarker).
    /// Сетевые объекты сцены (спавнер, голосование, ящики, бочки) не пересоздаются — только переносятся и «одеваются».
    /// Запуск: Maoshi → Setup → Stage 16 - Yard. Повторный запуск пересобирает окружение.
    /// </summary>
    public static class MaoshiSetupStage16
    {
        public const string TexturesFolder = ArtKit.ArtFolder + "/Textures";
        private const string EnvironmentName = "Environment";

        private static readonly string[] OldTestObjects = { "Ground", "Ramp", "Step1", "Step2", "Step3", "Fence" };

        // Материалы.
        private static Material _facadePanel, _facadeBlue, _facadeBrick, _asphalt, _grass, _sidewalk, _sand, _roofTar, _dirt, _door;
        private static Material[] _garageDoors, _cloth;
        private static Material _concrete, _plinth, _curb, _metalDark, _dumpster, _dumpsterLid, _wood, _woodDark, _roofSheet, _rubber,
            _red, _yellow, _blue, _green, _white, _glassDark, _lampGlow, _pigeon, _pigeonDark, _road, _roadLine, _brickWall, _balconyA, _balconyB;

        [MenuItem("Maoshi/Setup/Stage 16 - Yard")]
        public static void SetupStage16()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            CreateMaterials();

            var scene = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            foreach (var name in OldTestObjects)
            {
                var old = GameObject.Find(name);
                if (old != null && old.GetComponent<NetworkObject>() == null) Object.DestroyImmediate(old);
            }
            var oldEnv = GameObject.Find(EnvironmentName);
            if (oldEnv != null) Object.DestroyImmediate(oldEnv);

            var env = ArtKit.Group(EnvironmentName);
            BuildGround(env);
            BuildNorthHouse(env);
            BuildWestHouse(env);
            BuildGarages(env);
            BuildSouth(env);
            BuildBackdrop(env);
            BuildDumpsters(env);
            BuildSandbox(env);
            BuildPlayground(env);
            BuildDovecote(env);
            BuildLaundry(env);
            BuildDominoTable(env);
            BuildGreenery(env);
            BuildLamps(env);
            BuildParkedCars(env);
            BuildZoneMarkers(env);
            DressNetworkProps();
            SetupLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Повторное открытие и сохранение: NGO генерирует ID сетевых объектов сцены при загрузке.
            var reopened = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);

            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 16 готов: двор оформлен.");
        }

        // ───────────────────────── Материалы ─────────────────────────

        public static string Tex(string name) => $"{TexturesFolder}/{name}.png";

        private static void CreateMaterials()
        {
            _facadePanel = ArtKit.Textured("FacadePanel", Tex("Facade_Panel"));
            _facadeBlue = ArtKit.Textured("FacadePanelBlue", Tex("Facade_PanelBlue"));
            _facadeBrick = ArtKit.Textured("FacadeBrick", Tex("Facade_Brick"));
            _asphalt = ArtKit.Textured("AsphaltTex", Tex("Asphalt"));
            _grass = ArtKit.Textured("GrassTex", Tex("Grass"));
            _sidewalk = ArtKit.Textured("SidewalkTex", Tex("Sidewalk"));
            _sand = ArtKit.Textured("SandTex", Tex("Sand"));
            _roofTar = ArtKit.Textured("RoofTarTex", Tex("RoofTar"));
            _dirt = ArtKit.Textured("DirtTex", Tex("Dirt"));
            _door = ArtKit.Textured("DoorEntrance", Tex("Door_Entrance"));
            _garageDoors = new[]
            {
                ArtKit.Textured("GarageGreen", Tex("Garage_Green")),
                ArtKit.Textured("GarageBlue", Tex("Garage_Blue")),
                ArtKit.Textured("GarageRust", Tex("Garage_Rust")),
            };
            _cloth = new Material[5];
            for (int i = 0; i < _cloth.Length; i++) _cloth[i] = ArtKit.Textured("Cloth" + i, Tex("Cloth_" + i));

            _concrete = ArtKit.Color("Concrete", new Color(0.72f, 0.70f, 0.66f));
            _plinth = ArtKit.Color("Plinth", new Color(0.46f, 0.44f, 0.43f));
            _curb = ArtKit.Color("Curb", new Color(0.80f, 0.79f, 0.76f));
            _metalDark = ArtKit.Color("MetalDark", new Color(0.24f, 0.25f, 0.28f), 0.35f);
            _dumpster = ArtKit.Color("Dumpster", new Color(0.22f, 0.45f, 0.30f), 0.3f);
            _dumpsterLid = ArtKit.Color("DumpsterLid", new Color(0.16f, 0.32f, 0.22f), 0.3f);
            _wood = ArtKit.Color("YardWood", new Color(0.66f, 0.47f, 0.28f));
            _woodDark = ArtKit.Color("YardWoodDark", new Color(0.42f, 0.30f, 0.20f));
            _roofSheet = ArtKit.Color("RoofSheet", new Color(0.50f, 0.30f, 0.26f));
            _rubber = ArtKit.Color("Rubber", new Color(0.12f, 0.12f, 0.13f));
            _red = ArtKit.Color("PlayRed", new Color(0.86f, 0.28f, 0.24f));
            _yellow = ArtKit.Color("PlayYellow", new Color(0.98f, 0.78f, 0.22f));
            _blue = ArtKit.Color("PlayBlue", new Color(0.26f, 0.52f, 0.85f));
            _green = ArtKit.Color("PlayGreen", new Color(0.36f, 0.70f, 0.38f));
            _white = ArtKit.Color("PaintWhite", new Color(0.94f, 0.93f, 0.90f));
            _glassDark = ArtKit.Color("GlassDark", new Color(0.16f, 0.20f, 0.26f), 0.7f);
            _pigeon = ArtKit.Color("PigeonGreyArt", new Color(0.56f, 0.59f, 0.67f));
            _pigeonDark = ArtKit.Color("PigeonDarkArt", new Color(0.35f, 0.37f, 0.45f));
            _road = ArtKit.Color("Road", new Color(0.27f, 0.27f, 0.30f));
            _roadLine = ArtKit.Color("RoadLine", new Color(0.95f, 0.94f, 0.88f));
            _brickWall = ArtKit.Color("BrickWallPlain", new Color(0.66f, 0.36f, 0.28f));
            _balconyA = ArtKit.Color("BalconyA", new Color(0.55f, 0.66f, 0.74f));
            _balconyB = ArtKit.Color("BalconyB", new Color(0.82f, 0.78f, 0.68f));

            _lampGlow = ArtKit.Color("LampGlow", new Color(1f, 0.86f, 0.55f));
            _lampGlow.EnableKeyword("_EMISSION");
            _lampGlow.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.45f) * 1.6f);
            _lampGlow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(_lampGlow);
        }

        // ───────────────────────── Земля ─────────────────────────

        private static void BuildGround(Transform env)
        {
            var g = ArtKit.Group("Ground", env);
            ArtKit.UvBox("Asphalt", new Vector3(0f, -1f, -5f), new Vector3(150f, 1f, 150f), _asphalt, _asphalt, new Vector2(6f, 6f), 6f, g);

            // Тротуары вдоль домов.
            ArtKit.Patch("SidewalkNorth", new Vector3(0f, 0f, 22.5f), new Vector2(56f, 3f), _sidewalk, 2f, g);
            ArtKit.Patch("SidewalkWest", new Vector3(-26.5f, 0f, 2f), new Vector2(3f, 44f), _sidewalk, 2f, g);

            // Газоны под окнами северного дома (с проходами к подъездам) и бордюр.
            float[] xs = { -28f, -16.5f, -13.5f, -1.5f, 1.5f, 13.5f, 16.5f, 28f };
            for (int i = 0; i < xs.Length; i += 2)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                ArtKit.Patch($"LawnNorth{i / 2}", new Vector3((x0 + x1) * 0.5f, 0f, 19f), new Vector2(x1 - x0, 4f), _grass, 4f, g, 0.03f);
                ArtKit.Box($"CurbNorth{i / 2}", new Vector3((x0 + x1) * 0.5f, 0.06f, 16.95f), new Vector3(x1 - x0, 0.12f, 0.15f), _curb, g, false);
            }
            foreach (float x in new[] { -15f, 0f, 15f })
                ArtKit.Patch($"Path{x}", new Vector3(x, 0f, 19f), new Vector2(3f, 4f), _sidewalk, 2f, g, 0.025f);

            // Газоны у забора и под деревьями.
            ArtKit.Patch("LawnSouthWest", new Vector3(-22f, 0f, -17.5f), new Vector2(11f, 4.5f), _grass, 4f, g, 0.03f);
            ArtKit.Patch("LawnSouthEast", new Vector3(21.5f, 0f, -17.5f), new Vector2(12f, 4.5f), _grass, 4f, g, 0.03f);
            ArtKit.Patch("LawnWest", new Vector3(-22.5f, 0f, 16f), new Vector2(5f, 4f), _grass, 4f, g, 0.03f);
            ArtKit.Patch("LawnMiddle", new Vector3(-3f, 0f, -16f), new Vector2(10f, 4f), _grass, 4f, g, 0.03f);

            // Дорожки из плитки: от калитки к среднему подъезду и поперёк двора.
            ArtKit.Patch("WalkNS", new Vector3(0f, 0f, -1.5f), new Vector2(3f, 36.5f), _sidewalk, 2f, g, 0.022f);
            ArtKit.Patch("WalkEW", new Vector3(-1f, 0f, -1f), new Vector2(42f, 2.6f), _sidewalk, 2f, g, 0.021f);

            // Лужи, люки, классики мелом, разметка парковки.
            var puddle = ArtKit.Color("Puddle", new Color(0.20f, 0.21f, 0.24f), 0.92f);
            var manhole = ArtKit.Color("Manhole", new Color(0.20f, 0.20f, 0.22f), 0.4f);
            var chalk = ArtKit.Color("Chalk", new Color(0.95f, 0.95f, 0.92f));
            foreach (var p in new[] { new Vector3(-6f, 0f, -7f), new Vector3(12f, 0f, -9f), new Vector3(-15f, 0f, 2.5f), new Vector3(6f, 0f, 1.8f) })
            {
                var d = ArtKit.Cylinder("Puddle", p, 1f, 0.012f, puddle, g, false);
                d.transform.localScale = new Vector3(2.2f + Mathf.Abs(p.x) * 0.05f, 0.006f, 1.4f);
                d.transform.position = p + Vector3.up * 0.012f;
            }
            foreach (var p in new[] { new Vector3(4f, 0f, -14f), new Vector3(-13f, 0f, 11f), new Vector3(16f, 0f, 9.5f) })
            {
                var m = ArtKit.Cylinder("Manhole", p, 0.4f, 0.03f, manhole, g, false);
                m.transform.position = p + Vector3.up * 0.015f;
            }
            Vector3 h = new Vector3(4.5f, 0.03f, -4.5f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 cell = h + new Vector3(i % 3 == 1 ? 0f : (i % 3 == 0 ? -0.35f : 0.35f), 0f, i * 0.6f);
                ArtKit.Box("HopFrame", cell + new Vector3(0f, 0f, -0.3f), new Vector3(0.7f, 0.004f, 0.04f), chalk, g, false);
                ArtKit.Box("HopFrameL", cell + new Vector3(-0.35f, 0f, 0f), new Vector3(0.04f, 0.004f, 0.6f), chalk, g, false);
                ArtKit.Box("HopFrameR", cell + new Vector3(0.35f, 0f, 0f), new Vector3(0.04f, 0.004f, 0.6f), chalk, g, false);
            }
            for (float z = -4.2f; z < 14f; z += 2.6f)
                ArtKit.Box("ParkingLine", new Vector3(-23.5f, 0.012f, z), new Vector3(2.6f, 0.006f, 0.1f), chalk, g, false);
        }

        // ───────────────────────── Дома ─────────────────────────

        private const float Floor = 2.8f;

        /// <summary>Северная панельная пятиэтажка: фасад на юг (z = 24), три подъезда.</summary>
        private static void BuildNorthHouse(Transform env)
        {
            var g = ArtKit.Group("HouseNorth", env);
            const float front = 24f, depth = 12f, width = 56f, floors = 5;
            float height = Floor * floors;
            ArtKit.UvBox("Building", new Vector3(0f, 0f, front + depth * 0.5f), new Vector3(width, height, depth), _facadePanel, _roofTar,
                new Vector2(6f, Floor * 2f), 4f, g);
            ArtKit.Box("Plinth", new Vector3(0f, 0.3f, front + depth * 0.5f), new Vector3(width + 0.3f, 0.6f, depth + 0.3f), _plinth, g, false);
            Parapet(g, new Vector3(0f, height, front + depth * 0.5f), width, depth);

            foreach (float x in new[] { -15f, 0f, 15f })
                Entrance(g, new Vector3(x, 0f, front), 0f);

            // Балконы (кроме первого этажа и колонн подъездов).
            var rng = new System.Random(5);
            for (int floor = 1; floor < floors; floor++)
            {
                for (float x = -25.5f; x < 27f; x += 3f)
                {
                    if (Mathf.Abs(x % 15f) < 2f) continue; // над подъездами
                    if (x == -7.5f && floor == 1) continue;  // балкон бабушки строим отдельно
                    if (rng.NextDouble() > 0.33) continue;
                    Balcony(g, new Vector3(x, floor * Floor, front), 0f, rng.NextDouble() < 0.5 ? _balconyA : _balconyB, rng.NextDouble() < 0.35);
                }
            }

            GrandmaBalcony(g, new Vector3(-7.5f, Floor, front));

            // Антенны и выход на крышу.
            ArtKit.Box("RoofHatch", new Vector3(8f, height + 0.6f, 31f), new Vector3(2.2f, 1.2f, 2.2f), _concrete, g);
            foreach (var p in new[] { new Vector3(-18f, height, 28f), new Vector3(-4f, height, 33f), new Vector3(14f, height, 27f), new Vector3(22f, height, 33f) })
                ArtKit.PlaceFit("Furniture/televisionAntenna", p, 30f, new Vector3(2.4f, 1.4f, 1.4f), g);

            // Подвал: приямок со спуском и дверью.
            Basement(g, new Vector3(21.5f, 0f, front));
        }

        /// <summary>Западная кирпичная «хрущёвка»: фасад на восток (x = −28), двускатная крыша, слуховое окно.</summary>
        private static void BuildWestHouse(Transform env)
        {
            var g = ArtKit.Group("HouseWest", env);
            const float front = -28f, depth = 12f, length = 44f, floors = 4;
            float height = Floor * floors;
            Vector3 center = new Vector3(front - depth * 0.5f, 0f, 2f);
            ArtKit.UvBox("Building", center, new Vector3(depth, height, length), _facadeBrick, _roofTar, new Vector2(6f, Floor * 2f), 4f, g);
            ArtKit.Box("Plinth", center + Vector3.up * 0.3f, new Vector3(depth + 0.3f, 0.6f, length + 0.3f), _plinth, g, false);

            var roof = ArtKit.GableRoof("Roof", center + Vector3.up * height, length + 0.8f, depth + 1.2f, 3.2f, _roofSheet, g);
            roof.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            foreach (float z in new[] { -10f, 2f, 14f })
                Entrance(g, new Vector3(front, 0f, z), -90f);

            // Слуховое окно (чердак) на восточном скате.
            Vector3 dormer = new Vector3(front - 2.6f, height + 0.6f, 4f);
            ArtKit.Box("Dormer", dormer + Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 1.8f), _brickWall, g);
            var dormerRoof = ArtKit.GableRoof("DormerRoof", dormer + Vector3.up * 1.4f, 2.2f, 2.2f, 0.7f, _roofSheet, g);
            dormerRoof.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            ArtKit.Box("DormerWindow", new Vector3(front - 1.68f, dormer.y + 0.7f, dormer.z), new Vector3(0.05f, 0.8f, 0.9f), _glassDark, g, false);

            // Трубы.
            foreach (float z in new[] { -12f, 4f, 18f })
                ArtKit.Box("Chimney", new Vector3(front - depth * 0.5f, height + 3f, z), new Vector3(0.9f, 1.6f, 0.9f), _brickWall, g);
        }

        private static void Parapet(Transform g, Vector3 topCenter, float width, float depth)
        {
            float t = 0.25f, h = 0.6f;
            ArtKit.Box("ParapetS", topCenter + new Vector3(0f, h * 0.5f, -depth * 0.5f), new Vector3(width, h, t), _concrete, g);
            ArtKit.Box("ParapetN", topCenter + new Vector3(0f, h * 0.5f, depth * 0.5f), new Vector3(width, h, t), _concrete, g);
            ArtKit.Box("ParapetW", topCenter + new Vector3(-width * 0.5f, h * 0.5f, 0f), new Vector3(t, h, depth), _concrete, g);
            ArtKit.Box("ParapetE", topCenter + new Vector3(width * 0.5f, h * 0.5f, 0f), new Vector3(t, h, depth), _concrete, g);
        }

        /// <summary>Подъезд: дверь, козырёк, ступени, лампа, лавочки и урна. yaw 0 — фасад смотрит на −Z, 90 — на +X.</summary>
        private static void Entrance(Transform g, Vector3 wallPoint, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 outward = rot * Vector3.back;   // от стены во двор
            Vector3 along = rot * Vector3.right;

            ArtKit.Box("Door", wallPoint + Vector3.up * 1.2f + outward * 0.02f, Vector3.Scale(Abs(rot * new Vector3(1.3f, 2.2f, 0.06f)), Vector3.one), _door, g, false);
            ArtKit.Box("Canopy", wallPoint + Vector3.up * 2.75f + outward * 0.9f, Abs(rot * new Vector3(2.8f, 0.14f, 1.8f)), _concrete, g);
            ArtKit.Box("Steps", wallPoint + Vector3.up * 0.07f + outward * 0.75f, Abs(rot * new Vector3(2.4f, 0.14f, 1.5f)), _concrete, g, false);
            var lamp = ArtKit.Box("Lamp", wallPoint + Vector3.up * 2.55f + outward * 0.15f, new Vector3(0.25f, 0.18f, 0.25f), _lampGlow, g, false);
            lamp.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Лавочки по бокам и урна.
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 benchPos = wallPoint + outward * 2.6f + along * (side * 2.6f);
                ArtKit.PlaceFit("Graveyard/bench", benchPos, yaw + 180f, new Vector3(1.9f, 1.4f, 1.0f), g, true);
            }
            ArtKit.PlaceFit("Furniture/trashcan", wallPoint + outward * 2.4f + along * 1.25f, yaw, new Vector3(0.55f, 0.8f, 0.55f), g, true);
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void Balcony(Transform g, Vector3 wallBase, float yaw, Material panel, bool laundry)
        {
            var b = ArtKit.Group("Balcony", g);
            b.position = wallBase;
            b.rotation = Quaternion.Euler(0f, yaw, 0f);
            ArtKit.Box("Slab", wallBase + new Vector3(0f, 0.06f, -0.6f), new Vector3(2.7f, 0.12f, 1.2f), _concrete, b);
            ArtKit.Box("Front", wallBase + new Vector3(0f, 0.6f, -1.15f), new Vector3(2.7f, 1.0f, 0.08f), panel, b);
            ArtKit.Box("SideL", wallBase + new Vector3(-1.31f, 0.6f, -0.6f), new Vector3(0.08f, 1.0f, 1.2f), panel, b);
            ArtKit.Box("SideR", wallBase + new Vector3(1.31f, 0.6f, -0.6f), new Vector3(0.08f, 1.0f, 1.2f), panel, b);
            if (laundry)
            {
                ArtKit.Box("Rope", wallBase + new Vector3(0f, 1.6f, -1.0f), new Vector3(2.5f, 0.02f, 0.02f), _white, b, false);
                ArtKit.Quad("Shirt", wallBase + new Vector3(-0.6f, 1.25f, -1.0f), new Vector2(0.6f, 0.7f), _cloth[1], b);
                ArtKit.Quad("Towel", wallBase + new Vector3(0.4f, 1.2f, -1.0f), new Vector2(0.7f, 0.8f), _cloth[3], b);
            }
        }

        /// <summary>Балкон бабушки: цветы в горшках, бельё, кресло-качалка не влезло — табуретка.</summary>
        private static void GrandmaBalcony(Transform g, Vector3 wallBase)
        {
            Balcony(g, wallBase, 0f, _balconyB, true);
            var flowers = new[] { "Furniture/plantSmall1", "Furniture/plantSmall2", "Furniture/plantSmall3", "Furniture/pottedPlant" };
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = wallBase + new Vector3(-1.1f + i * 0.55f, 1.12f, -1.15f);
                ArtKit.PlaceFit(flowers[i % flowers.Length], p, i * 40f, new Vector3(0.4f, 0.5f, 0.4f), g);
            }
            ArtKit.Box("FlowerBox", wallBase + new Vector3(0f, 1.05f, -1.15f), new Vector3(2.6f, 0.14f, 0.3f), _woodDark, g, false);
        }

        private static void Basement(Transform g, Vector3 wallPoint)
        {
            ArtKit.Box("BasementWalls", wallPoint + new Vector3(0f, 0.45f, -1.0f), new Vector3(2.6f, 0.9f, 2.0f), _concrete, g);
            ArtKit.Box("BasementRoof", wallPoint + new Vector3(0f, 0.95f, -1.0f), new Vector3(2.9f, 0.1f, 2.3f), _metalDark, g, false);
            ArtKit.Box("BasementDoor", wallPoint + new Vector3(0f, 0.45f, -2.02f), new Vector3(1.2f, 0.75f, 0.05f), _metalDark, g, false);
            ArtKit.Box("BasementSign", wallPoint + new Vector3(0f, 0.75f, -2.06f), new Vector3(0.5f, 0.12f, 0.02f), _yellow, g, false);
        }

        // ───────────────────────── Гаражи и юг ─────────────────────────

        private static void BuildGarages(Transform env)
        {
            var g = ArtKit.Group("Garages", env);
            const float x0 = 22f, depth = 6f, height = 2.6f, w = 3.5f;
            var rng = new System.Random(9);
            int index = 0;
            for (float z = -12.25f; z <= 13f; z += w, index++)
            {
                ArtKit.UvBox($"Garage{index}", new Vector3(x0 + depth * 0.5f, 0f, z), new Vector3(depth, height, w - 0.05f), _concrete, _roofTar,
                    new Vector2(4f, 4f), 4f, g);
                ArtKit.Box($"GarageRoof{index}", new Vector3(x0 + depth * 0.5f - 0.15f, height + 0.06f, z), new Vector3(depth + 0.4f, 0.12f, w), _roofTar, g, false);
                ArtKit.Box($"GarageDoor{index}", new Vector3(x0 - 0.02f, 1.15f, z), new Vector3(0.05f, 2.2f, 2.9f), _garageDoors[rng.Next(_garageDoors.Length)], g, false);
            }

            // Хлам на крышах гаражей.
            foreach (var p in new[] { new Vector3(24f, height + 0.12f, -9f), new Vector3(26f, height + 0.12f, 6f) })
            {
                var tire = ArtKit.Cylinder("Tire", p, 0.38f, 0.22f, _rubber, g, false);
                tire.transform.localScale = new Vector3(0.76f, 0.11f, 0.76f);
            }

            // Кирпичная стена за гаражами и до дома — двор закрыт.
            ArtKit.Box("WallEast", new Vector3(28.2f, 1.4f, 2f), new Vector3(0.4f, 2.8f, 44f), _brickWall, g);
        }

        private static void BuildSouth(Transform env)
        {
            var g = ArtKit.Group("South", env);

            // Чугунный забор с невидимой стенкой (кот не перепрыгнет).
            for (float x = -27f; x < 28f; x += 2f)
                ArtKit.PlaceFit("Graveyard/ironFence", new Vector3(x, 0f, -20f), 0f, new Vector3(2.02f, 1.7f, 0.2f), g);
            var wall = ArtKit.Box("FenceCollider", new Vector3(0.5f, 1.6f, -20f), new Vector3(57f, 3.2f, 0.3f), _concrete, g);
            wall.GetComponent<Renderer>().enabled = false;
            ArtKit.Box("FenceBase", new Vector3(0.5f, 0.1f, -20f), new Vector3(57f, 0.2f, 0.4f), _plinth, g, false);

            // Улица за забором.
            ArtKit.Patch("SidewalkSouth", new Vector3(0f, 0f, -22.5f), new Vector2(150f, 4.5f), _sidewalk, 2f, g);
            ArtKit.Box("Road", new Vector3(0f, -0.04f, -30f), new Vector3(150f, 0.1f, 10f), _road, g, false);
            ArtKit.Box("CurbSouth", new Vector3(0f, 0.06f, -24.8f), new Vector3(150f, 0.14f, 0.25f), _curb, g, false);
            for (float x = -70f; x < 70f; x += 6f)
                ArtKit.Box("RoadLine", new Vector3(x, 0.02f, -30f), new Vector3(3f, 0.02f, 0.18f), _roadLine, g, false);

            string[] cars = { "Cars/sedan", "Cars/taxi", "Cars/van", "Cars/hatchbackSports", "Cars/police", "Cars/suv" };
            float[] carX = { -24f, -14f, -6f, 9f, 18f, 30f };
            for (int i = 0; i < carX.Length; i++)
                ArtKit.PlaceFit(cars[i], new Vector3(carX[i], 0f, -26.6f), 90f, new Vector3(4.4f, 2f, 2f), g, true);

            foreach (float x in new[] { -30f, -10f, 10f, 30f })
                Lamp(g, new Vector3(x, 0f, -23.8f), 0f);
        }

        /// <summary>Фон: дома вокруг двора (их видно поверх крыш), за ними туман.</summary>
        private static void BuildBackdrop(Transform env)
        {
            var g = ArtKit.Group("Backdrop", env);
            ArtKit.UvBox("FarNorth", new Vector3(-6f, 0f, 58f), new Vector3(66f, Floor * 9f, 12f), _facadeBlue, _roofTar, new Vector2(6f, Floor * 2f), 4f, g);
            ArtKit.UvBox("FarEast", new Vector3(46f, 0f, 10f), new Vector3(12f, Floor * 9f, 54f), _facadePanel, _roofTar, new Vector2(6f, Floor * 2f), 4f, g);
            ArtKit.UvBox("FarSouth", new Vector3(0f, 0f, -45f), new Vector3(80f, Floor * 5f, 12f), _facadeBrick, _roofTar, new Vector2(6f, Floor * 2f), 4f, g);
            ArtKit.UvBox("FarWest", new Vector3(-62f, 0f, 0f), new Vector3(12f, Floor * 9f, 70f), _facadeBlue, _roofTar, new Vector2(6f, Floor * 2f), 4f, g);
            foreach (var p in new[] { new Vector3(-40f, 0f, -36f), new Vector3(-25f, 0f, -37f), new Vector3(25f, 0f, -36f), new Vector3(40f, 0f, -37f), new Vector3(34f, 0f, 30f), new Vector3(36f, 0f, -12f) })
                Tree(g, "Nature/tree_default", p, 8f, false);
        }

        // ───────────────────────── Места двора ─────────────────────────

        private static void BuildDumpsters(Transform env)
        {
            var g = ArtKit.Group("Dumpsters", env);
            Vector3 c = new Vector3(-17f, 0f, -14.5f);
            ArtKit.Patch("Pad", c + new Vector3(0f, 0f, -0.2f), new Vector2(8.6f, 4.6f), _concrete, 4f, g, 0.04f);
            ArtKit.Box("BackWall", c + new Vector3(0f, 0.7f, -2.4f), new Vector3(8.6f, 1.4f, 0.25f), _brickWall, g);
            ArtKit.Box("SideWallW", c + new Vector3(-4.2f, 0.7f, -0.6f), new Vector3(0.25f, 1.4f, 3.8f), _brickWall, g);
            ArtKit.Box("SideWallE", c + new Vector3(4.2f, 0.7f, -0.6f), new Vector3(0.25f, 1.4f, 3.8f), _brickWall, g);

            for (int i = 0; i < 3; i++)
            {
                Vector3 p = c + new Vector3(-2.6f + i * 2.6f, 0f, -1.2f);
                ArtKit.Box($"Dumpster{i}", p + new Vector3(0f, 0.7f, 0f), new Vector3(2.0f, 1.1f, 1.3f), _dumpster, g);
                var lid = ArtKit.Box($"DumpsterLid{i}", p + new Vector3(0f, 1.3f, 0.05f), new Vector3(2.06f, 0.08f, 1.4f), _dumpsterLid, g, false);
                lid.transform.rotation = Quaternion.Euler(i == 1 ? 18f : 4f, 0f, 0f);
                foreach (float wx in new[] { -0.8f, 0.8f })
                {
                    var wheel = ArtKit.Cylinder("Wheel", p + new Vector3(wx, 0f, -0.55f), 0.14f, 0.12f, _rubber, g, false);
                    wheel.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    wheel.transform.position = p + new Vector3(wx, 0.14f, -0.55f);
                }
            }

            // Мешки с мусором, банки, рыбьи кости.
            var rng = new System.Random(3);
            for (int i = 0; i < 7; i++)
            {
                var bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bag.name = "TrashBag";
                Object.DestroyImmediate(bag.GetComponent<Collider>());
                bag.transform.SetParent(g, false);
                float s = 0.5f + (float)rng.NextDouble() * 0.25f;
                bag.transform.localScale = new Vector3(s, s * 0.8f, s);
                bag.transform.position = c + new Vector3(-3.6f + (float)rng.NextDouble() * 7.2f, s * 0.38f, 0.3f + (float)rng.NextDouble() * 0.9f);
                bag.GetComponent<Renderer>().sharedMaterial = _rubber;
                bag.isStatic = true;
            }
            ArtKit.PlaceFit("Food/fishBones", c + new Vector3(1.2f, 0.04f, 1.6f), 30f, new Vector3(0.6f, 0.2f, 0.6f), g);
            ArtKit.PlaceFit("Food/fishBones", c + new Vector3(-2f, 0.04f, 1.9f), 140f, new Vector3(0.5f, 0.2f, 0.5f), g);
            ArtKit.PlaceFit("Food/can", c + new Vector3(0.2f, 0.04f, 1.4f), 0f, new Vector3(0.15f, 0.2f, 0.15f), g);
            ArtKit.PlaceFit("Furniture/trashcan", c + new Vector3(5f, 0f, -1.4f), 0f, new Vector3(0.6f, 0.9f, 0.6f), g, true);
        }

        private static void BuildSandbox(Transform env)
        {
            var g = ArtKit.Group("Sandbox", env);
            Vector3 c = new Vector3(-9f, 0f, 7f);
            ArtKit.Patch("Sand", c, new Vector2(4.8f, 4.8f), _sand, 3f, g, 0.06f);
            ArtKit.Box("BorderN", c + new Vector3(0f, 0.12f, 2.5f), new Vector3(5.2f, 0.24f, 0.22f), _wood, g, false);
            ArtKit.Box("BorderS", c + new Vector3(0f, 0.12f, -2.5f), new Vector3(5.2f, 0.24f, 0.22f), _wood, g, false);
            ArtKit.Box("BorderW", c + new Vector3(-2.5f, 0.12f, 0f), new Vector3(0.22f, 0.24f, 5.2f), _wood, g, false);
            ArtKit.Box("BorderE", c + new Vector3(2.5f, 0.12f, 0f), new Vector3(0.22f, 0.24f, 5.2f), _wood, g, false);

            // «Грибок».
            ArtKit.Cylinder("MushroomPole", c + new Vector3(1.4f, 0f, 1.4f), 0.08f, 2.3f, _yellow, g);
            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "MushroomCap";
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.transform.SetParent(g, false);
            cap.transform.position = c + new Vector3(1.4f, 2.3f, 1.4f);
            cap.transform.localScale = new Vector3(2.4f, 0.7f, 2.4f);
            cap.GetComponent<Renderer>().sharedMaterial = _red;
            cap.isStatic = true;

            // Ведёрко и лопатка.
            ArtKit.Cylinder("Bucket", c + new Vector3(-1f, 0.06f, -0.6f), 0.14f, 0.25f, _blue, g, false);
            ArtKit.PlaceFit("Graveyard/shovel", c + new Vector3(-0.4f, 0.07f, -0.9f), 60f, new Vector3(0.2f, 0.1f, 0.6f), g);
        }

        private static void BuildPlayground(Transform env)
        {
            var g = ArtKit.Group("Playground", env);
            Vector3 c = new Vector3(9f, 0f, 8f);
            ArtKit.Patch("Surface", c, new Vector2(10f, 8f), _dirt, 3f, g, 0.035f);

            // Горка: башенка с площадкой, лесенка, скат (по скату можно забраться!).
            Vector3 t = c + new Vector3(-2.8f, 0f, 0.5f);
            foreach (var o in new[] { new Vector3(-0.6f, 0f, -0.6f), new Vector3(0.6f, 0f, -0.6f), new Vector3(-0.6f, 0f, 0.6f), new Vector3(0.6f, 0f, 0.6f) })
                ArtKit.Cylinder("Post", t + o, 0.07f, 2.4f, _blue, g);
            ArtKit.Box("Platform", t + new Vector3(0f, 1.5f, 0f), new Vector3(1.4f, 0.12f, 1.4f), _wood, g);
            ArtKit.Box("RailW", t + new Vector3(-0.66f, 1.95f, 0f), new Vector3(0.06f, 0.8f, 1.3f), _yellow, g);
            ArtKit.Box("RailE", t + new Vector3(0.66f, 1.95f, 0f), new Vector3(0.06f, 0.8f, 1.3f), _yellow, g);
            var roof = ArtKit.GableRoof("TowerRoof", t + Vector3.up * 2.4f, 1.6f, 1.6f, 0.7f, _red, g);
            roof.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            var slide = ArtKit.Box("Slide", t + new Vector3(0f, 0.8f, 1.95f), new Vector3(0.9f, 0.08f, 3.1f), _red, g);
            slide.transform.rotation = Quaternion.Euler(29f, 0f, 0f);
            ArtKit.PlaceFit("Platformer/ladder", t + new Vector3(0f, 0f, -1.0f), 0f, new Vector3(0.9f, 1.6f, 0.6f), g);

            // Качели.
            Vector3 s = c + new Vector3(2f, 0f, 2.2f);
            foreach (float x in new[] { -1.6f, 1.6f })
            {
                var legA = ArtKit.Box("SwingLeg", s + new Vector3(x, 1.2f, -0.45f), new Vector3(0.1f, 2.6f, 0.1f), _blue, g);
                legA.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);
                var legB = ArtKit.Box("SwingLeg", s + new Vector3(x, 1.2f, 0.45f), new Vector3(0.1f, 2.6f, 0.1f), _blue, g);
                legB.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            }
            ArtKit.Box("SwingBar", s + new Vector3(0f, 2.45f, 0f), new Vector3(3.4f, 0.1f, 0.1f), _blue, g, false);
            foreach (float x in new[] { -0.7f, 0.7f })
            {
                ArtKit.Box("ChainL", s + new Vector3(x - 0.22f, 1.5f, 0f), new Vector3(0.03f, 1.9f, 0.03f), _metalDark, g, false);
                ArtKit.Box("ChainR", s + new Vector3(x + 0.22f, 1.5f, 0f), new Vector3(0.03f, 1.9f, 0.03f), _metalDark, g, false);
                ArtKit.Box("Seat", s + new Vector3(x, 0.55f, 0f), new Vector3(0.55f, 0.06f, 0.3f), _yellow, g);
            }

            // Карусель.
            Vector3 k = c + new Vector3(2.3f, 0f, -2f);
            ArtKit.Cylinder("CarouselBase", k, 0.12f, 0.3f, _metalDark, g, false);
            ArtKit.Cylinder("CarouselDisk", k + Vector3.up * 0.3f, 1.2f, 0.1f, _green, g);
            ArtKit.Cylinder("CarouselPole", k + Vector3.up * 0.4f, 0.05f, 0.8f, _metalDark, g, false);
            for (int i = 0; i < 4; i++)
            {
                var bar = ArtKit.Box("Handle", k + Vector3.up * 1.15f, new Vector3(2.0f, 0.05f, 0.05f), _yellow, g, false);
                bar.transform.rotation = Quaternion.Euler(0f, i * 45f, 0f);
            }
        }

        private static void BuildDovecote(Transform env)
        {
            var g = ArtKit.Group("Dovecote", env);
            Vector3 c = new Vector3(15f, 0f, -14f);
            foreach (var o in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
                ArtKit.Box("Leg", c + o + Vector3.up * 1.5f, new Vector3(0.16f, 3f, 0.16f), _woodDark, g);
            ArtKit.Box("House", c + new Vector3(0f, 3.9f, 0f), new Vector3(2.5f, 1.8f, 2.5f), _wood, g);
            ArtKit.Box("Landing", c + new Vector3(0f, 3.1f, -1.5f), new Vector3(2.5f, 0.08f, 0.6f), _woodDark, g, false);
            for (int i = 0; i < 3; i++)
                ArtKit.Box("Hole", c + new Vector3(-0.8f + i * 0.8f, 3.6f, -1.26f), new Vector3(0.45f, 0.5f, 0.03f), _glassDark, g, false);
            ArtKit.GableRoof("Roof", c + Vector3.up * 4.8f, 3.0f, 3.0f, 1.1f, _roofSheet, g);
            var ladder = ArtKit.PlaceFit("Platformer/ladder", c + new Vector3(0f, 0f, -2.3f), 0f, new Vector3(1f, 3.1f, 1f), g);
            if (ladder != null) ladder.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);

            StaticPigeon(g, c + new Vector3(-0.6f, 5.85f, 0f), 80f);
            StaticPigeon(g, c + new Vector3(0.5f, 5.85f, 0f), -100f);
            StaticPigeon(g, c + new Vector3(0.9f, 3.15f, -1.55f), 180f);
            StaticPigeon(g, c + new Vector3(-3f, 0.02f, -2.6f), 30f);
        }

        private static void StaticPigeon(Transform g, Vector3 p, float yaw)
        {
            var root = ArtKit.Group("Pigeon", g);
            root.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.14f, 0f), new Vector3(0.26f, 0.22f, 0.36f), _pigeon);
            Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.3f, 0.15f), new Vector3(0.15f, 0.15f, 0.15f), _pigeonDark);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.29f, 0.25f), new Vector3(0.035f, 0.035f, 0.07f), _yellow);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.16f, -0.22f), new Vector3(0.14f, 0.03f, 0.16f), _pigeonDark);
        }

        private static void Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.isStatic = true;
        }

        private static void BuildLaundry(Transform env)
        {
            var g = ArtKit.Group("Laundry", env);
            const float z = 13f, y = 2.35f;
            foreach (float x in new[] { -19f, -11f })
            {
                ArtKit.Cylinder("Pole", new Vector3(x, 0f, z), 0.07f, y, _metalDark, g);
                ArtKit.Box("Cross", new Vector3(x, y, z), new Vector3(0.08f, 0.08f, 1.6f), _metalDark, g, false);
            }

            var rng = new System.Random(4);
            foreach (float dz in new[] { -0.6f, 0f, 0.6f })
            {
                ArtKit.Box("Rope", new Vector3(-15f, y - 0.02f, z + dz), new Vector3(8f, 0.02f, 0.02f), _white, g, false);
                for (float x = -18.3f; x < -11.6f; x += 0.9f + (float)rng.NextDouble() * 0.6f)
                {
                    float w = 0.5f + (float)rng.NextDouble() * 0.5f, h = 0.5f + (float)rng.NextDouble() * 0.6f;
                    ArtKit.Quad("Cloth", new Vector3(x, y - 0.03f - h * 0.5f, z + dz), new Vector2(w, h), _cloth[rng.Next(_cloth.Length)], g, 0f);
                }
            }
        }

        private static void BuildDominoTable(Transform env)
        {
            var g = ArtKit.Group("DominoTable", env);
            Vector3 c = new Vector3(-11f, 0f, -4f);
            ArtKit.Patch("Pad", c, new Vector2(4f, 3.4f), _sidewalk, 2f, g, 0.03f);
            ArtKit.PlaceFit("Furniture/table", c, 0f, new Vector3(1.8f, 0.8f, 1.0f), g, true);
            ArtKit.PlaceFit("Graveyard/bench", c + new Vector3(0f, 0f, -1.1f), 0f, new Vector3(1.9f, 1.3f, 0.9f), g, true);
            ArtKit.PlaceFit("Graveyard/bench", c + new Vector3(0f, 0f, 1.1f), 180f, new Vector3(1.9f, 1.3f, 0.9f), g, true);
            for (int i = 0; i < 6; i++)
                ArtKit.Box("Domino", c + new Vector3(-0.5f + i * 0.2f, 0.82f, (i % 2) * 0.12f), new Vector3(0.06f, 0.02f, 0.12f), _white, g, false);
        }

        private static void BuildGreenery(Transform env)
        {
            var g = ArtKit.Group("Greenery", env);
            Tree(g, "Nature/tree_oak", new Vector3(-23f, 0f, 19.5f), 7.5f);
            Tree(g, "Nature/tree_default", new Vector3(-8.5f, 0f, 19.6f), 7f);
            Tree(g, "Nature/tree_oak", new Vector3(8.5f, 0f, 19.4f), 7.5f);
            Tree(g, "Nature/tree_tall", new Vector3(23f, 0f, 19.5f), 8f);
            Tree(g, "Nature/tree_detailed", new Vector3(-23.5f, 0f, -6f), 7f);
            Tree(g, "Nature/tree_fat", new Vector3(18f, 0f, 2f), 6.5f);
            Tree(g, "Nature/tree_default", new Vector3(2f, 0f, -16.5f), 7f);
            Tree(g, "Nature/tree_small", new Vector3(-6f, 0f, -16.8f), 5f);
            Tree(g, "Nature/tree_oak", new Vector3(19f, 0f, 13f), 7f);
            Tree(g, "Nature/tree_pineRoundA", new Vector3(-25f, 0f, -17.5f), 8f);
            Tree(g, "Nature/tree_pineRoundC", new Vector3(25f, 0f, -17.5f), 7f);
            Tree(g, "Nature/tree_detailed", new Vector3(-22.5f, 0f, 16f), 6.5f);

            // Кусты под окнами и у забора — сквозь них можно пробежать.
            string[] bushes = { "Nature/plant_bushDetailed", "Nature/plant_bush", "Nature/plant_bushLarge" };
            var rng = new System.Random(8);
            int index = 0;
            for (float x = -26f; x < 27f; x += 2.6f)
            {
                if (Mathf.Abs(x + 15f) < 2.2f || Mathf.Abs(x) < 2.2f || Mathf.Abs(x - 15f) < 2.2f || Mathf.Abs(x - 21.5f) < 2.5f) continue;
                float size = 1.1f + (float)rng.NextDouble() * 0.5f;
                ArtKit.PlaceFit(bushes[index++ % bushes.Length], new Vector3(x, 0f, 20.6f + (float)rng.NextDouble() * 0.6f), rng.Next(360),
                    new Vector3(size * 1.4f, size, size * 1.4f), g);
            }
            for (float x = -26f; x < 27f; x += 3.4f)
            {
                float size = 0.9f + (float)rng.NextDouble() * 0.4f;
                ArtKit.PlaceFit(bushes[index++ % bushes.Length], new Vector3(x, 0f, -18.8f), rng.Next(360), new Vector3(size * 1.4f, size, size * 1.4f), g);
            }

            // Цветы на газонах.
            string[] flowers = { "Nature/flower_redA", "Nature/flower_yellowA", "Nature/flower_purpleA", "Nature/flower_redB", "Nature/flower_yellowB", "Nature/flower_purpleB" };
            for (int i = 0; i < 40; i++)
            {
                float x = -26f + (float)rng.NextDouble() * 52f;
                if (Mathf.Abs(x + 15f) < 2f || Mathf.Abs(x) < 2f || Mathf.Abs(x - 15f) < 2f) continue;
                ArtKit.PlaceFit(flowers[rng.Next(flowers.Length)], new Vector3(x, 0.03f, 17.5f + (float)rng.NextDouble() * 2.5f), rng.Next(360), new Vector3(0.6f, 0.5f, 0.6f), g);
            }
            for (int i = 0; i < 30; i++)
            {
                Vector3 p = new Vector3(-26f + (float)rng.NextDouble() * 52f, 0.03f, -16f - (float)rng.NextDouble() * 3.5f);
                ArtKit.PlaceFit(rng.NextDouble() < 0.5 ? "Nature/grass_large" : "Nature/grass", p, rng.Next(360), new Vector3(0.9f, 0.5f, 0.9f), g);
            }
        }

        private static void Tree(Transform g, string model, Vector3 p, float height, bool collider = true)
        {
            var tree = ArtKit.PlaceFit(model, p, (p.x * 37f + p.z * 11f) % 360f, new Vector3(100f, height, 100f), g);
            if (tree == null || !collider) return;
            var trunk = tree.AddComponent<CapsuleCollider>();
            float s = tree.transform.lossyScale.y;
            trunk.direction = 1;
            trunk.radius = 0.3f / s;
            trunk.height = 3f / s;
            trunk.center = tree.transform.InverseTransformPoint(p + Vector3.up * 1.5f);
        }

        private static void BuildLamps(Transform env)
        {
            var g = ArtKit.Group("Lamps", env);
            foreach (var p in new[] { new Vector3(-20f, 0f, -8f), new Vector3(-5f, 0f, 15.5f), new Vector3(13f, 0f, -6f), new Vector3(-5f, 0f, -12.5f), new Vector3(20.5f, 0f, 16f), new Vector3(-24.5f, 0f, 12f) })
                Lamp(g, p, 0f);
        }

        private static void Lamp(Transform g, Vector3 p, float yaw)
        {
            var lamp = ArtKit.PlaceFit("Graveyard/lightpostSingle", p, yaw, new Vector3(2f, 4.4f, 2f), g);
            if (lamp == null) return;
            var trunk = lamp.AddComponent<CapsuleCollider>();
            float s = lamp.transform.lossyScale.y;
            trunk.direction = 1;
            trunk.radius = 0.12f / s;
            trunk.height = 4f / s;
            trunk.center = lamp.transform.InverseTransformPoint(p + Vector3.up * 2f);
        }

        private static void BuildParkedCars(Transform env)
        {
            var g = ArtKit.Group("ParkedCars", env);
            ArtKit.PlaceFit("Cars/sedan", new Vector3(-24.2f, 0f, -1.5f), 0f, new Vector3(2f, 2f, 4.3f), g, true);
            ArtKit.PlaceFit("Cars/van", new Vector3(-24.2f, 0f, 3.8f), 0f, new Vector3(2.1f, 2.4f, 4.6f), g, true);
            ArtKit.PlaceFit("Cars/taxi", new Vector3(-24.2f, 0f, 9f), 180f, new Vector3(2f, 2f, 4.3f), g, true);
            ArtKit.PlaceFit("Cars/hatchbackSports", new Vector3(17.5f, 0f, -4.5f), 90f, new Vector3(4.2f, 2f, 2f), g, true);
        }

        // ───────────────────────── Флажки зон ─────────────────────────

        private static void BuildZoneMarkers(Transform env)
        {
            // Флажки зон строит этап 34 (крупные флаги, круг зоны, столб света).
            MaoshiSetupStage34.BuildMarkers(ArtKit.Group("ZoneMarkers", env));
        }

        private static void Marker(Transform g, int zoneId, Vector3 basePoint, float poleHeight)
        {
            var root = ArtKit.Group($"Zone{zoneId}", g);
            root.position = basePoint;
            ArtKit.Cylinder("Pole", basePoint, 0.05f, poleHeight, _white, root, false);

            var flag = ArtKit.Group("Flag", root);
            flag.position = basePoint + Vector3.up * (poleHeight - 0.35f);
            var cloth = ArtKit.Box("Cloth", flag.position + new Vector3(0.48f, 0f, 0f), new Vector3(0.9f, 0.55f, 0.03f), _white, flag, false);
            cloth.isStatic = false;
            flag.gameObject.isStatic = false;

            var label = ArtKit.Label("Label", YardZones.NameOf(zoneId), basePoint + Vector3.up * (poleHeight + 0.45f), 0f, 0.055f, Color.white, root);
            label.gameObject.isStatic = false;
            label.gameObject.AddComponent<FaceCamera>();

            var marker = root.gameObject.AddComponent<ZoneMarker>();
            var so = new SerializedObject(marker);
            so.FindProperty("zoneId").intValue = zoneId;
            so.FindProperty("flag").objectReferenceValue = flag;
            so.FindProperty("cloth").objectReferenceValue = cloth.GetComponent<Renderer>();
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.isStatic = false;
        }

        // ───────────────────────── Сетевые предметы и свет ─────────────────────────

        /// <summary>Ящики — коробки у помойки, башня ящиков — у гаражей, бочки — у гаражей. Физика прежняя.</summary>
        private static void DressNetworkProps()
        {
            for (int i = 0; i < 5; i++)
            {
                var crate = GameObject.Find($"Crate{i}");
                if (crate == null) continue;
                crate.transform.position = new Vector3(-11.5f + (i % 3) * 0.9f, 0.35f, -10.6f - (i / 3) * 0.9f);
                crate.transform.rotation = Quaternion.Euler(0f, i * 23f, 0f);
                ArtKit.Dress(crate, i % 2 == 0 ? "Furniture/cardboardBoxClosed" : "Furniture/cardboardBoxOpen");
            }

            for (int i = 0; i < 3; i++)
            {
                var crate = GameObject.Find($"CrateTower{i}");
                if (crate == null) continue;
                crate.transform.position = new Vector3(20.6f, 0.35f + i * 0.71f, -15.5f);
                crate.transform.rotation = Quaternion.identity;
                ArtKit.Dress(crate, "Platformer/crate");
            }

            for (int i = 0; i < 3; i++)
            {
                var barrel = GameObject.Find($"Barrel{i}");
                if (barrel == null) continue;
                barrel.transform.position = new Vector3(20.8f - (i % 2) * 0.6f, 0.4f, -8.5f + i * 0.7f);
                ArtKit.Dress(barrel, "Platformer/barrel");
            }
        }

        private static void SetupLighting()
        {
            // Надписи над кругами голосования — всегда лицом к камере (иначе сзади читаются зеркально).
            foreach (var pad in Object.FindObjectsByType<VotePad>(FindObjectsSortMode.None))
            {
                var label = pad.transform.Find("Label");
                if (label != null && label.GetComponent<FaceCamera>() == null) label.gameObject.AddComponent<FaceCamera>();
            }

            ArtLibrary.Lighting(false, 45f, 170f);
        }
    }
}
