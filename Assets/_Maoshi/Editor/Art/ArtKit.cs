using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools.Art
{
    /// <summary>
    /// Инструменты оформления сцен: поставить модель Kenney, «одеть» коллайдер-заглушку в модель,
    /// собрать панельный дом с фасадом. Всё только в редакторе (вызывается из Setup-скриптов).
    /// </summary>
    public static class ArtKit
    {
        public const string KenneyRoot = KenneyImportSettings.Root;
        public const string ArtFolder = "Assets/_Maoshi/Art";
        public const string MaterialsFolder = ArtFolder + "/Materials";
        public const string MeshesFolder = ArtFolder + "/Meshes";

        private static readonly Dictionary<string, GameObject> Cache = new();

        // ───────────────────────── Модели ─────────────────────────

        /// <summary>Модель по короткому имени: "Nature/tree_oak".</summary>
        public static GameObject Model(string name)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{KenneyRoot}/{name}.fbx");
            if (model == null) Debug.LogWarning($"[Maoshi Art] Нет модели {name}");
            Cache[name] = model;
            return model;
        }

        /// <summary>Поставить модель: позиция — точка на земле под центром модели, поворот по Y, равномерный масштаб.</summary>
        public static GameObject Place(string name, Vector3 groundPosition, float yaw = 0f, float scale = 1f, Transform parent = null, bool collider = false)
        {
            var model = Model(name);
            if (model == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (parent != null) instance.transform.SetParent(parent, false);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.position = Vector3.zero;

            // Ставим так, чтобы низ модели касался земли, а центр по XZ был в точке.
            var bounds = RendererBounds(instance);
            Vector3 offset = new Vector3(groundPosition.x - bounds.center.x, groundPosition.y - bounds.min.y, groundPosition.z - bounds.center.z);
            instance.transform.position += offset;
            SetStatic(instance);
            if (collider) AddBoxCollider(instance);
            return instance;
        }

        /// <summary>Поставить модель, подогнав её под размер (по самой «тесной» оси, без искажений).</summary>
        public static GameObject PlaceFit(string name, Vector3 groundPosition, float yaw, Vector3 size, Transform parent = null, bool collider = false)
        {
            var model = Model(name);
            if (model == null) return null;
            var natural = NaturalSize(model);
            float scale = MinRatio(size, natural);
            return Place(name, groundPosition, yaw, scale, parent, collider);
        }

        /// <summary>
        /// «Одеть» объект-заглушку: спрятать его меш (коллайдер и физика остаются) и вставить внутрь модель,
        /// растянутую ровно по габаритам заглушки. uniform — сохранить пропорции модели (вписать).
        /// </summary>
        public static GameObject Dress(GameObject block, string name, bool uniform = false, float yaw = 0f, float extraScale = 1f)
        {
            var model = Model(name);
            if (block == null || model == null) return null;

            var old = block.transform.Find("Art");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var blockRenderer = block.GetComponent<Renderer>();
            Bounds target = blockRenderer != null ? blockRenderer.bounds : RendererBounds(block);
            if (blockRenderer != null) blockRenderer.enabled = false;

            // Модель в своём натуральном размере, повёрнутая как надо.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "Art";
            instance.transform.rotation = block.transform.rotation * Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one;
            instance.transform.position = Vector3.zero;
            var natural = RendererBounds(instance);

            Vector3 scale = new Vector3(
                SafeDiv(target.size.x, natural.size.x),
                SafeDiv(target.size.y, natural.size.y),
                SafeDiv(target.size.z, natural.size.z));
            if (uniform)
            {
                float s = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
                scale = Vector3.one * s;
            }
            scale *= extraScale;

            // Масштаб задаём в мире (повороты у нас только по Y, оси совпадают с габаритами).
            Vector3 lossy = block.transform.lossyScale;
            instance.transform.SetParent(block.transform, true);
            instance.transform.localScale = new Vector3(SafeDiv(scale.x, lossy.x), SafeDiv(scale.y, lossy.y), SafeDiv(scale.z, lossy.z));

            // Совмещаем низ и центр модели с низом и центром заглушки.
            var placed = RendererBounds(instance);
            Vector3 shift = new Vector3(target.center.x - placed.center.x, target.min.y - placed.min.y, target.center.z - placed.center.z);
            instance.transform.position += shift;
            if (block.isStatic) SetStatic(instance);
            return instance;
        }

        public static Vector3 NaturalSize(GameObject model)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = Vector3.zero;
            var size = RendererBounds(instance).size;
            Object.DestroyImmediate(instance);
            return size;
        }

        public static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>Коллайдер-коробка по габаритам модели (в локальных координатах корня).</summary>
        public static BoxCollider AddBoxCollider(GameObject go)
        {
            var bounds = RendererBounds(go);
            var box = go.AddComponent<BoxCollider>();
            Vector3 lossy = go.transform.lossyScale;
            box.center = go.transform.InverseTransformPoint(bounds.center);
            Vector3 local = Quaternion.Inverse(go.transform.rotation) * bounds.size;
            box.size = new Vector3(Mathf.Abs(SafeDiv(local.x, lossy.x)), Mathf.Abs(SafeDiv(local.y, lossy.y)), Mathf.Abs(SafeDiv(local.z, lossy.z)));
            return box;
        }

        public static void SetStatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        }

        private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? 1f : a / b;

        private static float MinRatio(Vector3 size, Vector3 natural) =>
            Mathf.Min(SafeDiv(size.x, natural.x), Mathf.Min(SafeDiv(size.y, natural.y), SafeDiv(size.z, natural.z)));

        // ───────────────────────── Материалы ─────────────────────────

        public static Material Color(string name, Color color, float smoothness = 0.12f)
        {
            MaoshiSetup.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(LitShader());
                AssetDatabase.CreateAsset(material, path);
            }

            color.a = 1f;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material Textured(string name, string texturePath, float smoothness = 0.1f)
        {
            MaoshiSetup.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(LitShader());
                AssetDatabase.CreateAsset(material, path);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_BaseColor", UnityEngine.Color.white);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader LitShader()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            return lit != null ? lit : Shader.Find("Standard");
        }

        // ───────────────────────── Простые блоки ─────────────────────────

        public static GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent = null, bool collider = true, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }

        public static GameObject Cylinder(string name, Vector3 baseCenter, float radius, float height, Material material, Transform parent = null, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = baseCenter + Vector3.up * (height * 0.5f);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }

        // ───────────────────────── Коробки с UV в метрах ─────────────────────────

        /// <summary>
        /// Коробка с развёрткой «в метрах»: текстура стен повторяется каждые sideTile (ширина × высота),
        /// верх — каждые topTile метров. Два подматериала: стены и верх/низ. Низ коробки — в точке baseCenter.
        /// </summary>
        public static GameObject UvBox(string name, Vector3 baseCenter, Vector3 size, Material sides, Material top,
            Vector2 sideTile, float topTile, Transform parent = null, bool collider = true, float yaw = 0f, float sideVOffset = 0f)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(baseCenter, Quaternion.Euler(0f, yaw, 0f));
            go.isStatic = true;

            var mesh = BuildBoxMesh(size, sideTile, topTile, sideVOffset);
            mesh.name = name + "_Mesh";
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { sides, top != null ? top : sides };

            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;
            }
            return go;
        }

        /// <summary>Плоская «плитка» без коллайдера (газон, песок, разметка) чуть над землёй.</summary>
        public static GameObject Patch(string name, Vector3 center, Vector2 size, Material material, float tile, Transform parent = null, float height = 0.02f, float yaw = 0f)
        {
            return UvBox(name, new Vector3(center.x, center.y, center.z), new Vector3(size.x, height, size.y), material, material,
                new Vector2(tile, tile), tile, parent, false, yaw);
        }

        private static Mesh BuildBoxMesh(Vector3 s, Vector2 sideTile, float topTile, float vOffset)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f, h = s.y;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var sides = new List<int>();
            var tops = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, List<int> list)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                for (int k = 0; k < 4; k++) normals.Add(n);
                list.Add(i); list.Add(i + 1); list.Add(i + 2);
                list.Add(i); list.Add(i + 2); list.Add(i + 3);
            }

            // Стены: u — вдоль стены (м / sideTile.x), v — высота (м / sideTile.y).
            void Wall(Vector3 left, Vector3 right, float length, Vector3 n)
            {
                float u = length / sideTile.x, v = h / sideTile.y, v0 = vOffset / sideTile.y;
                Quad(left, left + Vector3.up * h, right + Vector3.up * h, right, n,
                    new Vector2(0f, v0), new Vector2(0f, v0 + v), new Vector2(u, v0 + v), new Vector2(u, v0), sides);
            }

            Wall(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), s.x, Vector3.back);   // юг (−Z)
            Wall(new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz), s.x, Vector3.forward); // север (+Z)
            Wall(new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), s.z, Vector3.right);    // восток (+X)
            Wall(new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz), s.z, Vector3.left);   // запад (−X)

            float tu = s.x / topTile, tv = s.z / topTile;
            Quad(new Vector3(-hx, h, -hz), new Vector3(-hx, h, hz), new Vector3(hx, h, hz), new Vector3(hx, h, -hz), Vector3.up,
                new Vector2(0f, 0f), new Vector2(0f, tv), new Vector2(tu, tv), new Vector2(tu, 0f), tops);
            Quad(new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), Vector3.down,
                new Vector2(0f, tv), new Vector2(0f, 0f), new Vector2(tu, 0f), new Vector2(tu, tv), tops);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0);
            mesh.SetTriangles(tops, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// Перетекстурировать кубик-заглушку: меш заменяется кубом той же формы, но с развёрткой «в метрах»
        /// (текстура не растягивается по большим блокам). Коллайдер и физика не меняются.
        /// </summary>
        public static void Retexture(GameObject block, Material sides, Material top, Vector2 sideTile, float topTile)
        {
            if (block == null) return;
            var filter = block.GetComponent<MeshFilter>();
            var renderer = block.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) return;

            Vector3 s = block.transform.lossyScale;
            s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            var mesh = BuildBoxMesh(s, sideTile, topTile, 0f);
            // Геометрия — единичный куб с центром в нуле (как у примитива), развёртка — по реальному размеру.
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(vertices[i].x / s.x, vertices[i].y / s.y - 0.5f, vertices[i].z / s.z);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.name = block.name + "_Retex";
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { sides, top != null ? top : sides };
        }

        /// <summary>Спрятать видимость объекта (коллайдер остаётся).</summary>
        public static void Hide(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        /// <summary>Двускатная крыша (призма) вдоль оси X: ширина по X, глубина по Z, высота конька.</summary>
        public static GameObject GableRoof(string name, Vector3 baseCenter, float width, float depth, float height, Material material, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = baseCenter;
            go.isStatic = true;

            float hx = width * 0.5f, hz = depth * 0.5f;
            var a = new Vector3(-hx, 0f, -hz); var b = new Vector3(hx, 0f, -hz);
            var c = new Vector3(hx, 0f, hz); var d = new Vector3(-hx, 0f, hz);
            var r1 = new Vector3(-hx, height, 0f); var r2 = new Vector3(hx, height, 0f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            void Tri(Vector3 p, Vector3 q, Vector3 r)
            {
                int i = vertices.Count;
                vertices.Add(p); vertices.Add(q); vertices.Add(r);
                uvs.Add(new Vector2(p.x, p.z + p.y) * 0.5f); uvs.Add(new Vector2(q.x, q.z + q.y) * 0.5f); uvs.Add(new Vector2(r.x, r.z + r.y) * 0.5f);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
            // Скаты.
            Tri(a, r1, r2); Tri(a, r2, b);
            Tri(c, r2, r1); Tri(c, r1, d);
            // Фронтоны.
            Tri(d, r1, a); Tri(b, r2, c);

            var mesh = new Mesh { name = name + "_Mesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return go;
        }

        /// <summary>Плоский квад (тряпка на верёвке, табличка) без коллайдера, двусторонний за счёт двух граней.</summary>
        public static GameObject Quad(string name, Vector3 center, Vector2 size, Material material, Transform parent = null, float yaw = 0f, float pitch = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(pitch, yaw, 0f));
            go.transform.localScale = new Vector3(size.x, size.y, 0.02f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }

        /// <summary>Надпись в мире (таблички зон). Шрифт встроенный — с кириллицей.</summary>
        public static TextMesh Label(string name, string text, Vector3 position, float yaw, float size, Color color, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var mesh = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            mesh.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        public static Transform Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.isStatic = true;
            return go.transform;
        }
    }
}
