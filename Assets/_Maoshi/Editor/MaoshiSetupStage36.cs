using System.Collections.Generic;
using System.IO;
using Maoshi.Cat;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 36: аксессуары котов. В префаб кота на кости головы (Head) и шеи (Neck.1) добавляются
    /// выключенные объекты «Acc_ключ» из простых форм: цилиндр, корона, кепка, колпак, бантик, ковбойская шляпа,
    /// ошейник с колокольчиком, шарф, бабочка, очки, тёмные очки, повязка пирата. Включает их CatAppearance/CatPreview
    /// по выбору игрока (CatAccessories). Координаты — в позе префаба (кот смотрит вдоль +Z, голова ~ (0, 0.57, 0.59)).
    /// Запуск: Maoshi → Setup → Stage 36 - Cat Accessories (пересобирает аксессуары). Проверка: Maoshi → Debug → Capture Cat Accessories.
    /// </summary>
    public static class MaoshiSetupStage36
    {
        private const string MeshFolder = ArtKit.MeshesFolder + "/Accessories";

        // Ориентиры головы в позе префаба (из Maoshi → Debug → Dump Cat Bones).
        private static readonly Vector3 HeadTop = new(0f, 0.66f, 0.572f);
        private static readonly Vector3 EyeL = new(-0.038f, 0.594f, 0.635f);
        private static readonly Vector3 EyeR = new(0.038f, 0.594f, 0.635f);
        private static readonly Vector3 NeckCenter = new(0f, 0.515f, 0.5f);
        private static readonly Vector3 NeckAxis = new Vector3(0f, 0.43f, 0.9f).normalized;

        [MenuItem("Maoshi/Setup/Stage 36 - Cat Accessories")]
        public static void Setup()
        {
            MaoshiSetup.EnsureFolder(MeshFolder);
            var root = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                Build(root);
                PrefabUtility.SaveAsPrefabAsset(root, MaoshiSetupStage2.CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 36 готов: аксессуары котов в префабе.");
        }

        private static void Build(GameObject root)
        {
            Transform head = null, neck = null;
            // Старые аксессуары: сначала собрать, потом удалить (иначе в списке останутся уже удалённые дети).
            var old = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Acc_")) old.Add(t.gameObject);
            foreach (var go in old) if (go != null) Object.DestroyImmediate(go);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Neck.1") neck = t;
            }
            if (head == null || neck == null)
            {
                Debug.LogError("[Maoshi Setup] Не нашёл кости Head / Neck.1 в префабе кота.");
                return;
            }

            // Позы считаем от корня префаба: временно ставим его в ноль.
            Vector3 rootPos = root.transform.position;
            Quaternion rootRot = root.transform.rotation;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            TopHat(head);
            Crown(head);
            Cap(head);
            PartyHat(head);
            Bow(head);
            Cowboy(head);
            Collar(neck);
            Scarf(neck);
            BowTie(neck);
            Glasses(head, false);
            Glasses(head, true);
            Patch(head);
            TinFoil(head);
            Halo(head);
            Horns(head);
            Propeller(head);
            Bucket(head);
            Cucumber(head);
            PixelShades(head);
            Mustache(head);
            ClownNose(head);
            Monocle(head);
            Bread(head);
            GoldChain(neck);
            Tie(neck);
            Cape(neck);
            ConeOfShame(neck);
            Medal(neck);

            root.transform.SetPositionAndRotation(rootPos, rootRot);
        }

        // ───────────────────────── Кирпичики ─────────────────────────

        /// <summary>Пустой корень аксессуара в мировых координатах, потом цепляется к кости (масштаб кости гасится).</summary>
        private static Transform Accessory(string key, Transform bone)
        {
            var go = new GameObject("Acc_" + key);
            go.transform.position = Vector3.zero;
            return go.transform;
        }

        private static void Attach(Transform accessory, Transform bone)
        {
            accessory.SetParent(bone, true);
            foreach (var t in accessory.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = bone.gameObject.layer;
            accessory.gameObject.SetActive(false);
        }

        private static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Цилиндр нужного радиуса и высоты с центром основания в точке base, ось — up.</summary>
        private static GameObject Cyl(Transform parent, Vector3 basePoint, float radius, float height, Material material, Quaternion? tilt = null)
        {
            Quaternion rot = tilt ?? Quaternion.identity;
            return Part(PrimitiveType.Cylinder, parent, basePoint + rot * Vector3.up * (height * 0.5f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, rot);
        }

        private static Material Mat(string name, Color color, float smoothness = 0.25f) => ArtKit.Color("Acc" + name, color, smoothness);

        /// <summary>Кольцо из коротких брусков вокруг оси (ошейник, шарф, оправа очков).</summary>
        private static void Ring(Transform parent, Vector3 center, Vector3 axis, float radius, int segments, Vector3 segmentSize, System.Func<int, Material> material)
        {
            axis.Normalize();
            Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 b = Vector3.Cross(axis, a).normalized;
            for (int i = 0; i < segments; i++)
            {
                float t = i * Mathf.PI * 2f / segments;
                Vector3 radial = a * Mathf.Cos(t) + b * Mathf.Sin(t);
                Vector3 tangent = Vector3.Cross(axis, radial);
                Part(PrimitiveType.Cube, parent, center + radial * radius, segmentSize, material(i), Quaternion.LookRotation(tangent, radial));
            }
        }

        /// <summary>Низ кольца вокруг шеи (точка на кольце с наименьшей высотой, чуть вперёд).</summary>
        private static Vector3 NeckFront(float radius)
        {
            Vector3 a = Vector3.Cross(NeckAxis, Vector3.right).normalized;
            if (a.y > 0f) a = -a;
            return NeckCenter + a * radius;
        }

        private static Mesh ConeMesh()
        {
            string path = MeshFolder + "/Cone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            const int n = 20;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                int v = vertices.Count;
                vertices.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f));
                vertices.Add(new Vector3(0f, 1f, 0f));
                vertices.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f));
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                // дно
                int c = vertices.Count;
                vertices.Add(Vector3.zero);
                vertices.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f));
                vertices.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f));
                triangles.Add(c); triangles.Add(c + 2); triangles.Add(c + 1);
            }
            mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ───────────────────────── Голова ─────────────────────────

        private static void TopHat(Transform head)
        {
            var acc = Accessory("TopHat", head);
            var black = Mat("HatBlack", new Color(0.12f, 0.11f, 0.14f), 0.45f);
            var band = Mat("HatRed", new Color(0.8f, 0.15f, 0.18f));
            Quaternion tilt = Quaternion.Euler(-10f, 0f, 8f);
            Vector3 b = HeadTop + new Vector3(0f, -0.004f, -0.006f);
            Cyl(acc, b, 0.05f, 0.007f, black, tilt);
            Cyl(acc, b + tilt * Vector3.up * 0.006f, 0.032f, 0.075f, black, tilt);
            Cyl(acc, b + tilt * Vector3.up * 0.008f, 0.0335f, 0.014f, band, tilt);
            Attach(acc, head);
        }

        private static void Crown(Transform head)
        {
            var acc = Accessory("Crown", head);
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            var ruby = Mat("Ruby", new Color(0.85f, 0.1f, 0.25f), 0.8f);
            Vector3 b = HeadTop + new Vector3(0f, 0.008f, 0f);
            Cyl(acc, b, 0.042f, 0.02f, gold);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                Vector3 p = b + new Vector3(Mathf.Sin(a) * 0.034f, 0.027f, Mathf.Cos(a) * 0.034f);
                Part(PrimitiveType.Cube, acc, p, new Vector3(0.016f, 0.016f, 0.008f), gold, Quaternion.LookRotation(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a))) * Quaternion.Euler(0f, 0f, 45f));
            }
            Part(PrimitiveType.Sphere, acc, b + new Vector3(0f, 0.012f, 0.041f), Vector3.one * 0.012f, ruby);
            Attach(acc, head);
        }

        private static void Cap(Transform head)
        {
            var acc = Accessory("Cap", head);
            var blue = Mat("CapBlue", new Color(0.2f, 0.45f, 0.85f));
            var white = Mat("CapWhite", new Color(0.95f, 0.95f, 0.95f));
            Part(PrimitiveType.Sphere, acc, HeadTop + new Vector3(0f, 0.004f, -0.004f), new Vector3(0.125f, 0.07f, 0.13f), blue);
            Part(PrimitiveType.Cube, acc, HeadTop + new Vector3(0f, -0.002f, 0.075f), new Vector3(0.09f, 0.006f, 0.06f), blue, Quaternion.Euler(10f, 0f, 0f));
            Part(PrimitiveType.Sphere, acc, HeadTop + new Vector3(0f, 0.038f, -0.004f), Vector3.one * 0.014f, white);
            Attach(acc, head);
        }

        private static void PartyHat(Transform head)
        {
            var acc = Accessory("PartyHat", head);
            var pink = Mat("PartyPink", new Color(0.95f, 0.35f, 0.6f));
            var yellow = Mat("PartyYellow", new Color(1f, 0.85f, 0.25f));
            Quaternion tilt = Quaternion.Euler(-8f, 0f, -14f);
            var cone = new GameObject("Cone");
            cone.transform.SetParent(acc, false);
            cone.transform.SetPositionAndRotation(HeadTop + new Vector3(0.006f, -0.004f, 0f), tilt);
            cone.transform.localScale = new Vector3(0.06f, 0.1f, 0.06f);
            cone.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            cone.AddComponent<MeshRenderer>().sharedMaterial = pink;
            Cyl(acc, HeadTop + new Vector3(0.006f, -0.004f, 0f) + tilt * Vector3.up * 0.02f, 0.025f, 0.008f, yellow, tilt);
            Part(PrimitiveType.Sphere, acc, HeadTop + new Vector3(0.006f, -0.004f, 0f) + tilt * Vector3.up * 0.1f, Vector3.one * 0.02f, yellow);
            Attach(acc, head);
        }

        private static void Bow(Transform head)
        {
            // Бантик у основания правого уха, спереди — крупный, чтобы не тонул в шерсти.
            var acc = Accessory("Bow", head);
            var pink = Mat("BowPink", new Color(1f, 0.45f, 0.65f));
            var dark = Mat("BowPinkDark", new Color(0.85f, 0.28f, 0.5f));
            Vector3 c = new(0.05f, 0.676f, 0.614f);
            Quaternion r = Quaternion.Euler(-10f, 0f, -25f);
            Part(PrimitiveType.Sphere, acc, c, new Vector3(0.022f, 0.022f, 0.02f), dark);
            Part(PrimitiveType.Sphere, acc, c + r * new Vector3(-0.026f, 0f, 0f), new Vector3(0.04f, 0.03f, 0.016f), pink, r * Quaternion.Euler(0f, 0f, 12f));
            Part(PrimitiveType.Sphere, acc, c + r * new Vector3(0.026f, 0f, 0f), new Vector3(0.04f, 0.03f, 0.016f), pink, r * Quaternion.Euler(0f, 0f, -12f));
            Part(PrimitiveType.Cube, acc, c + r * new Vector3(-0.01f, -0.022f, -0.002f), new Vector3(0.01f, 0.026f, 0.004f), pink, r * Quaternion.Euler(0f, 0f, -18f));
            Part(PrimitiveType.Cube, acc, c + r * new Vector3(0.01f, -0.022f, -0.002f), new Vector3(0.01f, 0.026f, 0.004f), pink, r * Quaternion.Euler(0f, 0f, 18f));
            Attach(acc, head);
        }

        private static void Cowboy(Transform head)
        {
            // Уши у кота высокие: шляпа сидит низко на макушке, уши торчат сквозь широкие поля (как в прорезях),
            // тулья — между ушами. Края полей загнуты вверх. Строго по центру, без наклона.
            var acc = Accessory("Cowboy", head);
            var brown = Mat("CowboyBrown", new Color(0.6f, 0.38f, 0.2f));
            var dark = Mat("CowboyBand", new Color(0.3f, 0.18f, 0.1f));
            const float z = 0.583f;
            const float brimY = 0.676f;
            Cyl(acc, new Vector3(0f, 0.655f, z), 0.041f, brimY - 0.655f + 0.07f, brown);
            Cyl(acc, new Vector3(0f, brimY + 0.001f, z), 0.0425f, 0.014f, dark);
            Part(PrimitiveType.Cube, acc, new Vector3(0f, brimY + 0.07f, z), new Vector3(0.014f, 0.01f, 0.06f), dark);
            Part(PrimitiveType.Cylinder, acc, new Vector3(0f, brimY, z), new Vector3(0.24f, 0.004f, 0.2f), brown);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cube, acc, new Vector3(side * 0.118f, brimY + 0.014f, z), new Vector3(0.008f, 0.034f, 0.14f), brown, Quaternion.Euler(0f, 0f, side * -38f));
            Attach(acc, head);
        }

        private static void Spin(GameObject target, Vector3 axis, float speed, float bob = 0f)
        {
            var spin = target.AddComponent<AccessorySpin>();
            var so = new SerializedObject(spin);
            so.FindProperty("axis").vector3Value = axis;
            so.FindProperty("degreesPerSecond").floatValue = speed;
            so.FindProperty("bobHeight").floatValue = bob;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject ConeAt(Transform parent, Vector3 basePoint, Vector3 scale, Material material, Quaternion rotation)
        {
            var cone = new GameObject("Cone");
            cone.transform.SetParent(parent, false);
            cone.transform.SetPositionAndRotation(basePoint, rotation);
            cone.transform.localScale = scale;
            cone.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            cone.AddComponent<MeshRenderer>().sharedMaterial = material;
            return cone;
        }

        // ───────────────────────── С приколом: голова ─────────────────────────

        private static void TinFoil(Transform head)
        {
            var acc = Accessory("TinFoil", head);
            var foil = Mat("Foil", new Color(0.82f, 0.84f, 0.88f), 0.9f);
            Vector3 b = HeadTop + new Vector3(0f, -0.006f, -0.004f);
            ConeAt(acc, b, new Vector3(0.1f, 0.12f, 0.1f), foil, Quaternion.Euler(-4f, 0f, 6f));
            // Мятые грани фольги.
            var rng = new System.Random(4);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float h = 0.01f + (float)rng.NextDouble() * 0.07f;
                float r = 0.05f * (1f - h / 0.12f);
                Part(PrimitiveType.Cube, acc, b + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r), Vector3.one * 0.016f, foil,
                    Quaternion.Euler(rng.Next(0, 90), rng.Next(0, 90), rng.Next(0, 90)));
            }
            Attach(acc, head);
        }

        private static void Halo(Transform head)
        {
            var acc = Accessory("Halo", head);
            var gold = ArtKit.Color("AccHaloGold", new Color(1f, 0.9f, 0.4f), 0.8f);
            gold.EnableKeyword("_EMISSION");
            gold.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * 1.6f);
            EditorUtility.SetDirty(gold);
            var ring = new GameObject("HaloRing").transform;
            ring.SetParent(acc, false);
            ring.position = HeadTop + new Vector3(0f, 0.05f, -0.01f);
            ring.rotation = Quaternion.Euler(-25f, 0f, 0f); // наклон к зрителю — кольцо видно спереди
            Ring(ring, ring.position, ring.up, 0.048f, 20, new Vector3(0.014f, 0.01f, 0.018f), _ => gold);
            Spin(ring.gameObject, Vector3.up, 60f, 0.008f);
            Attach(acc, head);
        }

        private static void Horns(Transform head)
        {
            var acc = Accessory("Horns", head);
            var red = Mat("HornRed", new Color(0.85f, 0.12f, 0.1f), 0.5f);
            foreach (float side in new[] { -1f, 1f })
                ConeAt(acc, HeadTop + new Vector3(side * 0.03f, -0.006f, 0.035f), new Vector3(0.036f, 0.075f, 0.036f), red, Quaternion.Euler(18f, 0f, side * -16f));
            Attach(acc, head);
        }

        private static void Propeller(Transform head)
        {
            var acc = Accessory("Propeller", head);
            var cap = Mat("BeanieBlue", new Color(0.3f, 0.7f, 0.95f));
            var yellow = Mat("BeanieYellow", new Color(1f, 0.85f, 0.2f));
            var red = Mat("BeanieRed", new Color(0.95f, 0.25f, 0.2f));
            Part(PrimitiveType.Sphere, acc, HeadTop + new Vector3(0f, 0.002f, -0.004f), new Vector3(0.115f, 0.06f, 0.115f), cap);
            Part(PrimitiveType.Cube, acc, HeadTop + new Vector3(0f, -0.004f, 0.06f), new Vector3(0.07f, 0.005f, 0.035f), yellow, Quaternion.Euler(8f, 0f, 0f));
            Cyl(acc, HeadTop + new Vector3(0f, 0.028f, -0.004f), 0.004f, 0.022f, yellow);
            var prop = new GameObject("Propeller").transform;
            prop.SetParent(acc, false);
            prop.position = HeadTop + new Vector3(0f, 0.052f, -0.004f);
            Part(PrimitiveType.Cube, acc, prop.position, new Vector3(0.09f, 0.003f, 0.016f), red).transform.SetParent(prop, true);
            Part(PrimitiveType.Cube, acc, prop.position, new Vector3(0.016f, 0.003f, 0.09f), yellow).transform.SetParent(prop, true);
            Spin(prop.gameObject, Vector3.up, 900f);
            Attach(acc, head);
        }

        private static void Bucket(Transform head)
        {
            var acc = Accessory("Bucket", head);
            var metal = Mat("BucketMetal", new Color(0.6f, 0.64f, 0.7f), 0.7f);
            var rim = Mat("BucketRim", new Color(0.4f, 0.43f, 0.48f), 0.6f);
            Quaternion tilt = Quaternion.Euler(-8f, 0f, 12f);
            Vector3 b = HeadTop + new Vector3(0f, -0.02f, -0.004f);
            Cyl(acc, b, 0.06f, 0.09f, metal, tilt);
            Cyl(acc, b, 0.063f, 0.008f, rim, tilt);
            Cyl(acc, b + tilt * Vector3.up * 0.086f, 0.05f, 0.006f, rim, tilt);
            // Ручка свисает набок.
            Part(PrimitiveType.Cube, acc, b + tilt * new Vector3(0.064f, 0.03f, 0f), new Vector3(0.004f, 0.06f, 0.004f), rim, tilt * Quaternion.Euler(0f, 0f, 15f));
            Attach(acc, head);
        }

        private static void Cucumber(Transform head)
        {
            var acc = Accessory("Cucumber", head);
            var green = Mat("CucumberGreen", new Color(0.25f, 0.55f, 0.22f), 0.4f);
            var light = Mat("CucumberLight", new Color(0.55f, 0.8f, 0.45f));
            Vector3 c = HeadTop + new Vector3(0f, 0.018f, -0.006f);
            Part(PrimitiveType.Capsule, acc, c + new Vector3(0f, 0.008f, 0f), new Vector3(0.05f, 0.085f, 0.05f), green, Quaternion.Euler(0f, 30f, 90f));
            for (int i = -2; i <= 2; i++)
                Part(PrimitiveType.Sphere, acc, c + Quaternion.Euler(0f, 30f, 0f) * new Vector3(i * 0.028f, 0.032f, 0f), Vector3.one * 0.008f, light);
            Attach(acc, head);
        }

        private static void PixelShades(Transform head)
        {
            // «Пиксельные очки»: ступеньки из чёрных квадратиков и белые блики.
            var acc = Accessory("PixelShades", head);
            var black = Mat("Pixel", new Color(0.03f, 0.03f, 0.03f), 0.3f);
            var white = Mat("PixelWhite", Color.white, 0.3f);
            const float px = 0.011f;
            float z = EyeL.z + 0.03f, y0 = EyeL.y + 0.014f;
            Part(PrimitiveType.Cube, acc, new Vector3(0f, y0, z), new Vector3(px * 15f, px, px), black);
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = side * 0.04f;
                Part(PrimitiveType.Cube, acc, new Vector3(cx, y0 - px, z), new Vector3(px * 5f, px, px), black);
                Part(PrimitiveType.Cube, acc, new Vector3(cx, y0 - px * 2f, z), new Vector3(px * 5f, px, px), black);
                Part(PrimitiveType.Cube, acc, new Vector3(cx + px * 0.5f, y0 - px * 3f, z), new Vector3(px * 3f, px, px), black);
                Part(PrimitiveType.Cube, acc, new Vector3(cx - px * 1.5f, y0 - px, z + 0.002f), new Vector3(px, px, px), white);
                Part(PrimitiveType.Cube, acc, new Vector3(cx - px * 0.5f, y0 - px * 2f, z + 0.002f), new Vector3(px, px, px), white);
            }
            Attach(acc, head);
        }

        private static void Mustache(Transform head)
        {
            var acc = Accessory("Mustache", head);
            var black = Mat("MustacheBlack", new Color(0.12f, 0.08f, 0.06f), 0.2f);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Sphere, acc, new Vector3(side * 0.024f, 0.534f, 0.676f), new Vector3(0.05f, 0.014f, 0.016f), black, Quaternion.Euler(0f, side * 15f, side * 12f));
                Part(PrimitiveType.Sphere, acc, new Vector3(side * 0.052f, 0.542f, 0.668f), Vector3.one * 0.013f, black);
            }
            Attach(acc, head);
        }

        private static void ClownNose(Transform head)
        {
            var acc = Accessory("ClownNose", head);
            Part(PrimitiveType.Sphere, acc, new Vector3(0f, 0.568f, 0.69f), Vector3.one * 0.042f, Mat("ClownRed", new Color(0.95f, 0.12f, 0.12f), 0.85f));
            Attach(acc, head);
        }

        private static void Monocle(Transform head)
        {
            var acc = Accessory("Monocle", head);
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            Vector3 c = EyeR + new Vector3(0f, 0.002f, 0.024f);
            Ring(acc, c, Vector3.forward, 0.023f, 14, new Vector3(0.006f, 0.006f, 0.012f), _ => gold);
            for (int i = 1; i <= 4; i++)
                Part(PrimitiveType.Sphere, acc, c + new Vector3(0.012f + i * 0.006f, -0.022f - i * 0.016f, -0.004f * i), Vector3.one * 0.006f, gold);
            Attach(acc, head);
        }

        private static void Bread(Transform head)
        {
            // Мем «кот в хлебушке»: морда торчит из ломтика хлеба.
            var acc = Accessory("Bread", head);
            var crumb = Mat("BreadCrumb", new Color(0.98f, 0.88f, 0.66f), 0.05f);
            var crust = Mat("BreadCrust", new Color(0.72f, 0.45f, 0.2f), 0.1f);
            Vector3 c = new(0f, 0.58f, 0.666f);
            const float k = 0.78f; // размер ломтика
            const float t = 0.012f;
            // Мякиш вокруг морды (рамка) и корочка по краю.
            Part(PrimitiveType.Cube, acc, c + new Vector3(0f, 0.085f * k, 0f), new Vector3(0.2f * k, 0.04f * k, t), crumb);
            Part(PrimitiveType.Cube, acc, c + new Vector3(0f, -0.08f * k, 0f), new Vector3(0.2f * k, 0.035f * k, t), crumb);
            Part(PrimitiveType.Cube, acc, c + new Vector3(-0.085f * k, 0f, 0f), new Vector3(0.035f * k, 0.2f * k, t), crumb);
            Part(PrimitiveType.Cube, acc, c + new Vector3(0.085f * k, 0f, 0f), new Vector3(0.035f * k, 0.2f * k, t), crumb);
            // Шапочка ломтика — два «плеча» сверху.
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cylinder, acc, c + new Vector3(side * 0.06f * k, 0.1f * k, 0f), new Vector3(0.09f * k, t * 0.5f, 0.07f * k), crumb, Quaternion.Euler(90f, 0f, 0f));
            Part(PrimitiveType.Cube, acc, c + new Vector3(0f, 0.105f * k, -0.003f), new Vector3(0.23f * k, 0.06f * k, t * 0.8f), crust);
            Part(PrimitiveType.Cube, acc, c + new Vector3(0f, -0.1f * k, -0.003f), new Vector3(0.23f * k, 0.012f, t * 0.8f), crust);
            Part(PrimitiveType.Cube, acc, c + new Vector3(-0.108f * k, 0f, -0.003f), new Vector3(0.012f, 0.21f * k, t * 0.8f), crust);
            Part(PrimitiveType.Cube, acc, c + new Vector3(0.108f * k, 0f, -0.003f), new Vector3(0.012f, 0.21f * k, t * 0.8f), crust);
            Attach(acc, head);
        }

        // ───────────────────────── С приколом: шея ─────────────────────────

        private static void GoldChain(Transform neck)
        {
            var acc = Accessory("GoldChain", neck);
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            Ring(acc, NeckCenter + new Vector3(0f, -0.01f, 0f), NeckAxis, 0.094f, 22, new Vector3(0.016f, 0.012f, 0.02f), _ => gold);
            Vector3 front = NeckFront(0.104f);
            for (int i = 1; i <= 3; i++)
                Part(PrimitiveType.Sphere, acc, front + new Vector3(0f, -0.012f * i, 0.006f * i), Vector3.one * 0.012f, gold);
            Part(PrimitiveType.Cylinder, acc, front + new Vector3(0f, -0.055f, 0.026f), new Vector3(0.05f, 0.004f, 0.05f), gold, Quaternion.Euler(70f, 0f, 0f));
            Attach(acc, neck);
        }

        private static void Tie(Transform neck)
        {
            var acc = Accessory("Tie", neck);
            var red = Mat("TieRed", new Color(0.8f, 0.15f, 0.2f), 0.4f);
            var stripe = Mat("TieStripe", new Color(0.95f, 0.85f, 0.3f));
            Vector3 knot = NeckFront(0.1f) + new Vector3(0f, -0.004f, 0.02f);
            Quaternion down = Quaternion.Euler(-25f, 0f, 0f);
            Part(PrimitiveType.Cube, acc, knot, new Vector3(0.024f, 0.022f, 0.016f), red, down);
            Part(PrimitiveType.Cube, acc, knot + down * new Vector3(0f, -0.07f, 0f), new Vector3(0.034f, 0.12f, 0.008f), red, down);
            for (int i = 0; i < 3; i++)
                Part(PrimitiveType.Cube, acc, knot + down * new Vector3(0f, -0.035f - i * 0.03f, 0.005f), new Vector3(0.034f, 0.006f, 0.002f), stripe, down * Quaternion.Euler(0f, 0f, 30f));
            Attach(acc, neck);
        }

        private static void Cape(Transform neck)
        {
            // Плащ супергероя: воротник-кольцо с золотой застёжкой + живая ткань (CapeCloth) от плеч по спине,
            // снаружи красная, подкладка золотая, на спине — золотой значок с рыбкой. Ткань считает физику сама.
            var acc = Accessory("Cape", neck);
            var red = Mat("CapeRed", new Color(0.85f, 0.1f, 0.15f), 0.3f);
            var lining = Mat("CapeLining", new Color(1f, 0.76f, 0.22f), 0.45f);
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            var fish = Mat("CapeFish", new Color(0.62f, 0.06f, 0.1f), 0.3f);
            Ring(acc, NeckCenter, NeckAxis, 0.088f, 18, new Vector3(0.016f, 0.014f, 0.03f), _ => red);
            Part(PrimitiveType.Sphere, acc, NeckFront(0.1f), Vector3.one * 0.024f, gold);

            var cloth = new GameObject("Cloth");
            cloth.transform.SetParent(acc, false);
            cloth.transform.SetPositionAndRotation(new Vector3(0f, 0.612f, 0.43f), Quaternion.identity);
            cloth.AddComponent<MeshFilter>();
            var renderer = cloth.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { red, lining };

            // Значок: золотой кружок и рыбка на нём (в своих осях: вперёд — в ткань, вверх — к плечам).
            var emblem = new GameObject("Emblem").transform;
            emblem.SetParent(cloth.transform, false);
            emblem.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Part(PrimitiveType.Cylinder, emblem, Vector3.zero, new Vector3(0.07f, 0.002f, 0.07f), gold, Quaternion.Euler(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, emblem, new Vector3(-0.006f, 0f, -0.003f), new Vector3(0.036f, 0.02f, 0.004f), fish);
            Part(PrimitiveType.Cube, emblem, new Vector3(0.017f, 0f, -0.003f), new Vector3(0.014f, 0.014f, 0.004f), fish, Quaternion.Euler(0f, 0f, 45f));
            Part(PrimitiveType.Sphere, emblem, new Vector3(-0.016f, 0.003f, -0.006f), Vector3.one * 0.005f, gold);
            emblem.position = cloth.transform.position;

            var capeCloth = cloth.AddComponent<CapeCloth>();
            var so = new SerializedObject(capeCloth);
            so.FindProperty("emblem").objectReferenceValue = emblem;
            so.ApplyModifiedPropertiesWithoutUndo();
            Attach(acc, neck);
        }

        private static void ConeOfShame(Transform neck)
        {
            // «Воротник позора» — ветеринарный конус вокруг шеи, раструбом к морде.
            var acc = Accessory("Cone", neck);
            var plastic = Mat("ConePlastic", new Color(0.88f, 0.94f, 1f), 0.7f);
            var edge = Mat("ConeEdge", new Color(0.55f, 0.75f, 0.95f), 0.5f);
            const int rings = 5;
            for (int r = 0; r < rings; r++)
            {
                int ringIndex = r;
                float k = r / (float)(rings - 1);
                float radius = Mathf.Lerp(0.09f, 0.16f, k);
                Vector3 center = NeckCenter + NeckAxis * Mathf.Lerp(0.0f, 0.075f, k);
                Ring(acc, center, NeckAxis, radius, 28, new Vector3(0.022f, 0.004f, 2f * Mathf.PI * radius / 28f * 1.15f), _ => ringIndex == rings - 1 ? edge : plastic);
            }
            Attach(acc, neck);
        }

        private static void Medal(Transform neck)
        {
            var acc = Accessory("Medal", neck);
            var blue = Mat("MedalBlue", new Color(0.2f, 0.4f, 0.85f));
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            Vector3 front = NeckFront(0.1f) + new Vector3(0f, 0f, 0.016f);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cube, acc, front + new Vector3(side * 0.012f, -0.022f, 0f), new Vector3(0.016f, 0.05f, 0.004f), blue, Quaternion.Euler(-20f, 0f, side * 18f));
            Part(PrimitiveType.Cylinder, acc, front + new Vector3(0f, -0.055f, 0.012f), new Vector3(0.042f, 0.004f, 0.042f), gold, Quaternion.Euler(70f, 0f, 0f));
            Part(PrimitiveType.Cube, acc, front + new Vector3(0f, -0.055f, 0.016f), new Vector3(0.014f, 0.014f, 0.004f), blue, Quaternion.Euler(70f, 0f, 0f) * Quaternion.Euler(0f, 45f, 0f));
            Attach(acc, neck);
        }

        // ───────────────────────── Шея ─────────────────────────

        private static void Collar(Transform neck)
        {
            var acc = Accessory("Collar", neck);
            var red = Mat("CollarRed", new Color(0.85f, 0.15f, 0.15f));
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.18f), 0.75f);
            Ring(acc, NeckCenter, NeckAxis, 0.09f, 20, new Vector3(0.02f, 0.02f, 0.034f), _ => red);
            Part(PrimitiveType.Sphere, acc, NeckFront(0.104f) + new Vector3(0f, -0.012f, 0.01f), Vector3.one * 0.03f, gold);
            Attach(acc, neck);
        }

        private static void Scarf(Transform neck)
        {
            var acc = Accessory("Scarf", neck);
            var green = Mat("ScarfGreen", new Color(0.25f, 0.6f, 0.4f));
            var cream = Mat("ScarfCream", new Color(0.97f, 0.92f, 0.8f));
            Ring(acc, NeckCenter, NeckAxis, 0.088f, 18, new Vector3(0.028f, 0.034f, 0.035f), i => i % 2 == 0 ? green : cream);
            Vector3 front = NeckFront(0.1f);
            Part(PrimitiveType.Cube, acc, front + new Vector3(0.018f, -0.035f, 0.012f), new Vector3(0.026f, 0.06f, 0.01f), green, Quaternion.Euler(10f, 0f, 12f));
            Part(PrimitiveType.Cube, acc, front + new Vector3(0.022f, -0.068f, 0.016f), new Vector3(0.026f, 0.014f, 0.011f), cream, Quaternion.Euler(10f, 0f, 12f));
            Attach(acc, neck);
        }

        private static void BowTie(Transform neck)
        {
            var acc = Accessory("BowTie", neck);
            var black = Mat("TieBlack", new Color(0.13f, 0.12f, 0.16f), 0.5f);
            Vector3 c = NeckFront(0.1f) + new Vector3(0f, -0.006f, 0.026f);
            Quaternion face = Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.4f);
            Part(PrimitiveType.Cube, acc, c, new Vector3(0.016f, 0.016f, 0.012f), black, face);
            Part(PrimitiveType.Cube, acc, c + new Vector3(-0.022f, 0f, 0f), new Vector3(0.03f, 0.026f, 0.01f), black, face * Quaternion.Euler(0f, 0f, 45f));
            Part(PrimitiveType.Cube, acc, c + new Vector3(0.022f, 0f, 0f), new Vector3(0.03f, 0.026f, 0.01f), black, face * Quaternion.Euler(0f, 0f, 45f));
            Attach(acc, neck);
        }

        // ───────────────────────── Морда ─────────────────────────

        private static void Glasses(Transform head, bool dark)
        {
            var acc = Accessory(dark ? "Shades" : "Glasses", head);
            var frame = dark ? Mat("ShadesFrame", new Color(0.08f, 0.08f, 0.1f), 0.6f) : Mat("GlassesFrame", new Color(0.55f, 0.35f, 0.15f), 0.4f);
            var lens = dark ? Mat("ShadesLens", new Color(0.05f, 0.05f, 0.08f), 0.95f) : null;
            float z = 0.026f;
            foreach (var eye in new[] { EyeL, EyeR })
            {
                Vector3 c = eye + new Vector3(0f, 0.002f, z);
                Ring(acc, c, Vector3.forward, 0.02f, 12, new Vector3(0.006f, 0.006f, 0.012f), _ => frame);
                if (lens != null) Part(PrimitiveType.Cylinder, acc, c, new Vector3(0.04f, 0.002f, 0.04f), lens, Quaternion.Euler(90f, 0f, 0f));
            }
            Part(PrimitiveType.Cube, acc, new Vector3(0f, EyeL.y + 0.006f, EyeL.z + z), new Vector3(0.036f, 0.005f, 0.005f), frame);
            // Дужки к ушам.
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cube, acc, new Vector3(side * 0.06f, EyeL.y + 0.006f, EyeL.z + z - 0.03f), new Vector3(0.004f, 0.004f, 0.06f), frame, Quaternion.Euler(0f, side * -12f, 0f));
            Attach(acc, head);
        }

        private static void Patch(Transform head)
        {
            var acc = Accessory("Patch", head);
            var black = Mat("PatchBlack", new Color(0.12f, 0.1f, 0.1f), 0.2f);
            Vector3 c = EyeR + new Vector3(0f, 0.002f, 0.018f);
            Part(PrimitiveType.Cylinder, acc, c, new Vector3(0.042f, 0.004f, 0.036f), black, Quaternion.Euler(90f, 0f, 0f));
            // Ремешок наискосок через лоб.
            Part(PrimitiveType.Cube, acc, new Vector3(0.0f, 0.628f, 0.625f), new Vector3(0.11f, 0.005f, 0.006f), black, Quaternion.Euler(-25f, 0f, -28f));
            Attach(acc, head);
        }

        // ───────────────────────── Проверка ─────────────────────────

        /// <summary>Снимки кота с каждым аксессуаром (спереди и сбоку) → Temp/Captures/acc_*.png.</summary>
        [MenuItem("Maoshi/Debug/Capture Cat Accessories")]
        public static void Capture()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[Maoshi] Сохрани сцену."); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoshiSetupStage2.CatPrefabPath);
            var cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            cat.transform.position = Vector3.zero;

            var cam = Object.FindFirstObjectByType<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.32f, 0.5f);
            cam.fieldOfView = 22f;
            var rt = new RenderTexture(512, 512, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            Directory.CreateDirectory("Temp/Captures");

            var all = new Dictionary<string, GameObject>();
            foreach (var t in cat.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Acc_")) all[t.name.Substring(4)] = t.gameObject;

            void Shot(string file, Vector3 eye, float targetY = 0.6f, float targetZ = 0.58f)
            {
                Vector3 target = new(0f, targetY, targetZ);
                cam.transform.position = target + eye;
                cam.transform.LookAt(target);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"Temp/Captures/{file}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                RenderTexture.active = null;
            }

            foreach (var info in CatAccessories.All)
            {
                foreach (var pair in all) pair.Value.SetActive(pair.Key == info.Key);
                Shot("acc_" + info.Key + "_front", new Vector3(0.25f, 0.12f, 0.75f), 0.63f);
                Shot("acc_" + info.Key + "_back", new Vector3(-0.3f, 0.35f, -0.8f), 0.63f);
                Shot("acc_" + info.Key + "_side", new Vector3(0.8f, 0.15f, 0.05f));
                Shot("acc_" + info.Key + "_wide", new Vector3(0.9f, 0.35f, 1.1f), 0.5f, 0.4f);
            }

            // Плащ: поза покоя и бег (кота двигаем вперёд 6 м/с, физику шагаем вручную).
            if (all.TryGetValue("Cape", out var capeObject))
            {
                foreach (var pair in all) pair.Value.SetActive(pair.Key == "Cape");
                var cloth = capeObject.GetComponentInChildren<CapeCloth>(true);
                cloth.BuildRestPose();
                Shot("acc_Cape_rest", new Vector3(1.4f, 0.75f, -1.1f), 0.5f, 0.3f);
                Shot("acc_Cape_restback", new Vector3(-0.35f, 0.55f, -0.9f), 0.55f, 0.3f);
                for (int f = 0; f < 60; f++)
                {
                    cat.transform.position += Vector3.forward * (6f / 60f);
                    cloth.Tick(1f / 60f);
                    if (f == 59) Shot("acc_Cape_run", new Vector3(1.4f, 0.75f, -1.1f), 0.5f, cat.transform.position.z + 0.3f);
                }
                for (int f = 0; f < 12; f++) cloth.Tick(1f / 60f);
                Shot("acc_Cape_stop", new Vector3(1.4f, 0.75f, -1.1f), 0.5f, cat.transform.position.z + 0.3f);
                cat.transform.position = Vector3.zero;
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi] Снимки аксессуаров → Temp/Captures/acc_*.png");
        }
    }
}
