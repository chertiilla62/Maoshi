using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maoshi.EditorTools.Art
{
    /// <summary>
    /// Общие материалы и «окружение» для сцен мини-игр: дома вокруг площадки, деревья, фонари, машины,
    /// небо и туман. Площадка игры остаётся как была — всё ставится вокруг неё.
    /// </summary>
    public static class ArtLibrary
    {
        public const float Floor = 2.8f;

        public static Material Tex(string name, float smoothness = 0.1f) =>
            ArtKit.Textured(name + "Tex", $"{ArtKit.ArtFolder}/Textures/{name}.png", smoothness);

        public static Material FacadePanel => Tex("Facade_Panel");
        public static Material FacadeBlue => Tex("Facade_PanelBlue");
        public static Material FacadeBrick => Tex("Facade_Brick");
        public static Material Asphalt => Tex("Asphalt");
        public static Material Grass => Tex("Grass");
        public static Material Sidewalk => Tex("Sidewalk");
        public static Material Sand => Tex("Sand");
        public static Material RoofTar => Tex("RoofTar");
        public static Material Dirt => Tex("Dirt");
        public static Material Planks => Tex("FencePlanks");
        public static Material ShedPlanks => Tex("ShedPlanks");
        public static Material Cardboard => Tex("Cardboard");
        public static Material Parquet => Tex("Parquet");
        public static Material Concrete => Tex("Concrete");
        public static Material MetalSheet => Tex("MetalSheet", 0.3f);
        public static Material AcUnit => Tex("ACUnit");
        public static Material BrickPlain => Tex("BrickPlain");
        public static Material BrickYellow => Tex("BrickYellow");
        public static Material Wallpaper => Tex("Wallpaper");

        public static Material RoofSheet => ArtKit.Color("RoofSheet", new Color(0.50f, 0.30f, 0.26f));
        public static Material Wood => ArtKit.Color("YardWood", new Color(0.66f, 0.47f, 0.28f));
        public static Material WoodDark => ArtKit.Color("YardWoodDark", new Color(0.42f, 0.30f, 0.20f));
        public static Material MetalDark => ArtKit.Color("MetalDark", new Color(0.24f, 0.25f, 0.28f), 0.35f);
        public static Material Dumpster => ArtKit.Color("Dumpster", new Color(0.22f, 0.45f, 0.30f), 0.3f);
        public static Material Rubber => ArtKit.Color("Rubber", new Color(0.12f, 0.12f, 0.13f));
        public static Material GlassDark => ArtKit.Color("GlassDark", new Color(0.16f, 0.20f, 0.26f), 0.7f);
        public static Material Road => ArtKit.Color("Road", new Color(0.27f, 0.27f, 0.30f));
        public static Material White => ArtKit.Color("PaintWhite", new Color(0.94f, 0.93f, 0.90f));
        public static Material Curb => ArtKit.Color("Curb", new Color(0.80f, 0.79f, 0.76f));

        // ───────────────────────── Окружение ─────────────────────────

        /// <summary>
        /// Обставить площадку: земля под всем (на 3 см ниже, чтобы не спорить с землёй площадки),
        /// кольцо домов за пределами прямоугольника min..max (с запасом margin), деревья, фонари и машины между ними.
        /// </summary>
        public static Transform Surround(Vector2 min, Vector2 max, float margin = 8f, int seed = 1, float groundY = 0f, bool houses = true)
        {
            var old = GameObject.Find("Surroundings");
            if (old != null) Object.DestroyImmediate(old);
            var root = ArtKit.Group("Surroundings");
            var rng = new System.Random(seed);

            Vector2 center = (min + max) * 0.5f;
            ArtKit.UvBox("FarGround", new Vector3(center.x, groundY - 1.03f, center.y), new Vector3(260f, 1f, 260f), Asphalt, Asphalt, new Vector2(6f, 6f), 6f, root, false);

            // Газон-полоса вокруг площадки.
            float gx0 = min.x - margin * 0.6f, gx1 = max.x + margin * 0.6f, gz0 = min.y - margin * 0.6f, gz1 = max.y + margin * 0.6f;
            ArtKit.Patch("LawnS", new Vector3(center.x, groundY - 0.025f, gz0 + 1.5f), new Vector2(gx1 - gx0, 3f), Grass, 4f, root, 0.01f);
            ArtKit.Patch("LawnN", new Vector3(center.x, groundY - 0.025f, gz1 - 1.5f), new Vector2(gx1 - gx0, 3f), Grass, 4f, root, 0.01f);

            if (houses)
            {
                Material[] facades = { FacadePanel, FacadeBrick, FacadeBlue };
                float d = margin + 6f;
                // Север и юг — длинные дома, запад и восток — торцами.
                House(root, new Vector3(center.x, groundY, max.y + d + 6f), new Vector3(max.x - min.x + 2f * d + 12f, Floor * (5 + rng.Next(5)), 12f), facades[rng.Next(3)]);
                House(root, new Vector3(center.x, groundY, min.y - d - 6f), new Vector3(max.x - min.x + 2f * d - 10f, Floor * (5 + rng.Next(4)), 12f), facades[rng.Next(3)]);
                House(root, new Vector3(min.x - d - 6f, groundY, center.y), new Vector3(12f, Floor * (5 + rng.Next(5)), max.y - min.y + 2f * d - 8f), facades[rng.Next(3)]);
                House(root, new Vector3(max.x + d + 6f, groundY, center.y), new Vector3(12f, Floor * (4 + rng.Next(6)), max.y - min.y + 2f * d - 8f), facades[rng.Next(3)]);
            }

            // Деревья, фонари, машины, лавочки по периметру (за пределами площадки).
            string[] trees = { "Nature/tree_oak", "Nature/tree_default", "Nature/tree_detailed", "Nature/tree_tall", "Nature/tree_fat" };
            string[] cars = { "Cars/sedan", "Cars/taxi", "Cars/van", "Cars/hatchbackSports", "Cars/suv" };
            float perimeter = 2f * ((max.x - min.x) + (max.y - min.y) + 4f * margin);
            int count = Mathf.Clamp((int)(perimeter / 7f), 8, 40);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = PerimeterPoint(min, max, margin * (0.45f + (float)rng.NextDouble() * 0.4f), (float)i / count + (float)rng.NextDouble() * 0.02f, groundY);
                double roll = rng.NextDouble();
                if (roll < 0.55) Tree(root, trees[rng.Next(trees.Length)], p, 5.5f + (float)rng.NextDouble() * 3f);
                else if (roll < 0.7) Lamp(root, p);
                else if (roll < 0.85) ArtKit.PlaceFit(cars[rng.Next(cars.Length)], p, rng.Next(4) * 90f, new Vector3(4.3f, 2f, 4.3f), root);
                else ArtKit.PlaceFit("Graveyard/bench", p, rng.Next(4) * 90f, new Vector3(1.9f, 1.3f, 1.9f), root);
            }

            return root;
        }

        private static Vector3 PerimeterPoint(Vector2 min, Vector2 max, float offset, float t, float y)
        {
            t = t - Mathf.Floor(t);
            float w = max.x - min.x + 2f * offset, h = max.y - min.y + 2f * offset, p = 2f * (w + h);
            float d = t * p;
            float x0 = min.x - offset, z0 = min.y - offset;
            if (d < w) return new Vector3(x0 + d, y, z0);
            d -= w;
            if (d < h) return new Vector3(x0 + w, y, z0 + d);
            d -= h;
            if (d < w) return new Vector3(x0 + w - d, y, z0 + h);
            d -= w;
            return new Vector3(x0, y, z0 + h - d);
        }

        public static GameObject House(Transform parent, Vector3 baseCenter, Vector3 size, Material facade)
        {
            var house = ArtKit.UvBox("House", baseCenter, size, facade, RoofTar, new Vector2(6f, Floor * 2f), 4f, parent);
            ArtKit.Box("Plinth", baseCenter + Vector3.up * 0.3f, new Vector3(size.x + 0.3f, 0.6f, size.z + 0.3f), ArtKit.Color("Plinth", new Color(0.46f, 0.44f, 0.43f)), house.transform, false);
            return house;
        }

        public static GameObject Tree(Transform parent, string model, Vector3 p, float height, bool collider = true)
        {
            var tree = ArtKit.PlaceFit(model, p, (p.x * 37f + p.z * 11f) % 360f, new Vector3(100f, height, 100f), parent);
            if (tree == null || !collider) return tree;
            var trunk = tree.AddComponent<CapsuleCollider>();
            float s = tree.transform.lossyScale.y;
            trunk.direction = 1;
            trunk.radius = 0.3f / s;
            trunk.height = 3f / s;
            trunk.center = tree.transform.InverseTransformPoint(p + Vector3.up * 1.5f);
            return tree;
        }

        public static GameObject Lamp(Transform parent, Vector3 p)
        {
            var lamp = ArtKit.PlaceFit("Graveyard/lightpostSingle", p, 0f, new Vector3(2f, 4.4f, 2f), parent);
            if (lamp == null) return null;
            var trunk = lamp.AddComponent<CapsuleCollider>();
            float s = lamp.transform.lossyScale.y;
            trunk.direction = 1;
            trunk.radius = 0.12f / s;
            trunk.height = 4f / s;
            trunk.center = lamp.transform.InverseTransformPoint(p + Vector3.up * 2f);
            return lamp;
        }

        /// <summary>Мультяшное небо: процедурное, яркое, с маленьким солнцем.</summary>
        public static Material ToonSky()
        {
            string path = ArtKit.MaterialsFolder + "/M_SkyToon.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.55f);
            sky.SetColor("_SkyTint", new Color(0.56f, 0.70f, 0.92f));
            sky.SetColor("_GroundColor", new Color(0.62f, 0.66f, 0.70f));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>Солнце, небо, туман, окружающий свет (мультяшный: яркий, тёплый, тени с синевой). warm — закат.</summary>
        public static void Lighting(bool warm = false, float fogStart = 40f, float fogEnd = 160f)
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            if (sun != null)
            {
                sun.transform.rotation = warm ? Quaternion.Euler(30f, -50f, 0f) : Quaternion.Euler(50f, -35f, 0f);
                sun.color = warm ? new Color(1f, 0.84f, 0.66f) : new Color(1f, 0.96f, 0.88f);
                sun.intensity = warm ? 1.2f : 1.35f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.9f;
            }

            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data == null) data = camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
            }

            RenderSettings.skybox = ToonSky();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = warm ? new Color(0.86f, 0.78f, 0.80f) : new Color(0.72f, 0.80f, 0.96f);
            RenderSettings.ambientEquatorColor = warm ? new Color(0.86f, 0.70f, 0.60f) : new Color(0.78f, 0.78f, 0.76f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.42f, 0.40f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = warm ? new Color(0.96f, 0.82f, 0.72f) : new Color(0.76f, 0.86f, 0.97f);
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }
    }
}
