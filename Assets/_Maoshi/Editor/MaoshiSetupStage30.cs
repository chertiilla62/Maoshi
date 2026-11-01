using Maoshi.EditorTools.Art;
using Maoshi.Hub;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 30: новый выбор мини-игры и кошачья рулетка во дворе.
    /// - Голосование: четыре круга (три игры + «Кот в мешке»), каждый своего цвета, 45 секунд, круги светятся при выборе.
    /// - Кошачья рулетка у восточного края двора: колесо на стойке с вывеской и лампочками,
    ///   перед ним фанерный щит с тремя ковриками для ставок (рыжие ×2, золотая ×10, серые ×2).
    /// Запуск: Maoshi → Setup → Stage 30 - Vote And Roulette. Остальной двор не трогается.
    /// </summary>
    public static class MaoshiSetupStage30
    {
        private const string RootName = "CatRoulette";
        private const string TexFolder = MaoshiSetup.Root + "/Art/Textures";
        // Колесо смотрит на запад — к центру двора, откуда приходят коты; щит со ставками между ним и кругами голосования.
        private static readonly Vector3 RoulettePosition = new(10.6f, 0f, -4.2f);
        private const float RouletteYaw = 90f;

        [MenuItem("Maoshi/Setup/Stage 30 - Vote And Roulette")]
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

            ImportTextures();
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);

            SetupVoting();
            BuildRoulette();

            MaoshiSetupStage22.SaveWithNetworkIds(scene, MaoshiSetup.HubScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 30 готов: новое голосование и кошачья рулетка.");
        }

        // ───────────────────────── Текстуры ─────────────────────────

        private static void ImportTextures()
        {
            foreach (var name in new[] { "RouletteWheel", "RouletteSign", "RouletteMat" })
            {
                if (AssetImporter.GetAtPath($"{TexFolder}/{name}.png") is not TextureImporter importer) continue;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath("Assets/_Maoshi/Resources/Maoshi/UI/RouletteIcon.png") is TextureImporter icon)
            {
                icon.alphaIsTransparency = true;
                icon.mipmapEnabled = false;
                icon.wrapMode = TextureWrapMode.Clamp;
                icon.SaveAndReimport();
            }
        }

        private static Material Cutout(string name, string texture)
        {
            var material = ArtKit.Textured(name, $"{TexFolder}/{texture}.png", 0.25f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material Emissive(string name, Color color)
        {
            var material = ArtKit.Color(name, color, 0.5f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ───────────────────────── Голосование ─────────────────────────

        private static void SetupVoting()
        {
            var voting = Object.FindFirstObjectByType<HubVoting>();
            if (voting == null)
            {
                Debug.LogError("[Maoshi Setup] В сцене Hub нет HubVoting — сначала этап 4.");
                return;
            }

            // Круги пересоздаём: четыре, своего цвета (цвет задаёт HubVoting), со светящимся диском.
            foreach (var old in voting.GetComponentsInChildren<VotePad>(true))
                Object.DestroyImmediate(old.gameObject);

            var padMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaoshiSetup.Root + "/Materials/M_VotePad.mat");
            if (padMaterial != null)
            {
                padMaterial.EnableKeyword("_EMISSION");
                padMaterial.SetColor("_EmissionColor", Color.black);
                padMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(padMaterial);
            }
            var rim = ArtKit.Color("VotePadRim", new Color(0.13f, 0.09f, 0.16f));

            // Коты смотрят со спавна на юг: первый круг — слева (восток), номера идут слева направо.
            float[] xs = { 6.75f, 2.25f, -2.25f, -6.75f };
            var pads = new VotePad[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                pads[i] = CreatePad(voting.transform, $"VotePad{i}", new Vector3(xs[i], 0f, -9.5f), padMaterial, rim);

            var so = new SerializedObject(voting);
            var padsProperty = so.FindProperty("pads");
            padsProperty.arraySize = pads.Length;
            for (int i = 0; i < pads.Length; i++) padsProperty.GetArrayElementAtIndex(i).objectReferenceValue = pads[i];
            so.FindProperty("voteDuration").floatValue = 45f;
            so.FindProperty("allVotedRemaining").floatValue = 5f;
            so.FindProperty("avoidRecent").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static VotePad CreatePad(Transform parent, string name, Vector3 position, Material material, Material rim)
        {
            var padGo = new GameObject(name);
            padGo.transform.SetParent(parent, false);
            padGo.transform.position = position;

            // Тёмный обод под диском — круг читается на асфальте и видно, где кончается.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Rim";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(padGo.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            ring.transform.localScale = new Vector3(3.3f, 0.012f, 3.3f);
            ring.GetComponent<Renderer>().sharedMaterial = rim;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(padGo.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            disc.transform.localScale = new Vector3(3f, 0.02f, 3f);
            var discRenderer = disc.GetComponent<Renderer>();
            discRenderer.sharedMaterial = material;

            // Афиша за кругом (с южной стороны, лицом к спавну): шапка с номером и жанром, название, о чём игра, кто голосует.
            Vector3 P(float x, float y, float z) => position + new Vector3(x, y, z);
            const float back = -1.95f;
            var wood = ArtLibrary.WoodDark;
            var paper = ArtKit.Color("PosterPaper", new Color(1f, 0.95f, 0.86f), 0.05f);
            var tint = Emissive("PosterTint", Color.white);
            tint.SetColor("_EmissionColor", Color.black);
            foreach (float x in new[] { -1.2f, 1.2f })
                ArtKit.Box("PosterPost", P(x, 1.25f, back - 0.04f), new Vector3(0.09f, 2.5f, 0.09f), wood, padGo.transform);
            var frame = ArtKit.Box("PosterFrame", P(0f, 1.73f, back), new Vector3(2.5f, 1.52f, 0.06f), tint, padGo.transform);
            ArtKit.Box("PosterPaper", P(0f, 1.71f, back + 0.035f), new Vector3(2.3f, 1.32f, 0.03f), paper, padGo.transform, false);
            var header = ArtKit.Box("PosterHeader", P(0f, 2.22f, back + 0.045f), new Vector3(2.3f, 0.3f, 0.03f), tint, padGo.transform, false);
            float front = back + 0.07f;
            TextMesh Text(string n, float y, float size, bool bold)
            {
                var tm = ArtKit.Label(n, "", P(0f, y, front), 180f, size, Color.black, padGo.transform);
                tm.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                tm.richText = true;
                return tm;
            }
            var head = Text("PosterHead", 2.22f, 0.019f, true);
            var title = Text("PosterTitle", 1.92f, 0.034f, true);
            var tag = Text("PosterTag", 1.66f, 0.019f, true);
            var votes = Text("PosterVotes", 1.36f, 0.019f, true);
            foreach (var r in new[] { frame.GetComponent<Renderer>(), header.GetComponent<Renderer>() }) r.gameObject.isStatic = false;

            var pad = padGo.AddComponent<VotePad>();
            var so = new SerializedObject(pad);
            so.FindProperty("disc").objectReferenceValue = discRenderer;
            so.FindProperty("label").objectReferenceValue = null;
            so.FindProperty("posterFrame").objectReferenceValue = frame.GetComponent<Renderer>();
            so.FindProperty("posterHeader").objectReferenceValue = header.GetComponent<Renderer>();
            so.FindProperty("posterHead").objectReferenceValue = head;
            so.FindProperty("posterTitle").objectReferenceValue = title;
            so.FindProperty("posterTag").objectReferenceValue = tag;
            so.FindProperty("posterVotes").objectReferenceValue = votes;
            so.FindProperty("radius").floatValue = 1.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return pad;
        }

        // ───────────────────────── Рулетка ─────────────────────────

        private static void BuildRoulette()
        {
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            var wood = ArtLibrary.Wood;
            var woodDark = ArtLibrary.WoodDark;
            var red = ArtKit.Color("RouletteRed", new Color(0.62f, 0.18f, 0.16f), 0.3f);
            var brass = ArtKit.Color("RouletteBrass", new Color(0.92f, 0.74f, 0.32f), 0.6f);
            var bulb = Emissive("RouletteBulb", new Color(1f, 0.85f, 0.45f));
            var wheelFace = Cutout("RouletteWheel", "RouletteWheel");
            var sign = ArtKit.Textured("RouletteSign", $"{TexFolder}/RouletteSign.png", 0.2f);
            var mat = ArtKit.Textured("RouletteMat", $"{TexFolder}/RouletteMat.png", 0.15f);

            // Строим в локальных координатах (корень в нуле), в конце ставим корень на место и поворачиваем.
            var root = new GameObject(RootName);
            root.AddComponent<NetworkObject>();
            var roulette = root.AddComponent<CatRoulette>();
            var t = root.transform;
            Vector3 P(float x, float y, float z) => new(x, y, z);

            // Стойка: основание, два столба, балка за колесом, перекладина с язычком, вывеска.
            ArtKit.Box("Base", P(0f, 0.08f, 0.15f), new Vector3(3.0f, 0.16f, 1.1f), woodDark, t);
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                ArtKit.Box("Post", P(x, 1.95f, 0.15f), new Vector3(0.16f, 3.6f, 0.16f), red, t);
                var strut = ArtKit.Box("Strut", P(x, 0.75f, 0.5f), new Vector3(0.1f, 1.3f, 0.1f), woodDark, t, false);
                strut.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);
            }
            ArtKit.Box("Beam", P(0f, 1.75f, 0.2f), new Vector3(2.7f, 0.14f, 0.14f), woodDark, t, false);
            ArtKit.Box("TopBar", P(0f, 3.05f, 0.15f), new Vector3(2.86f, 0.14f, 0.16f), red, t, false);
            var axle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            axle.name = "Axle";
            Object.DestroyImmediate(axle.GetComponent<Collider>());
            axle.transform.SetParent(t, false);
            axle.transform.SetPositionAndRotation(P(0f, 1.75f, 0.07f), Quaternion.Euler(90f, 0f, 0f));
            axle.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            axle.GetComponent<Renderer>().sharedMaterial = brass;
            ArtKit.Quad("Sign", P(0f, 3.48f, 0.12f), new Vector2(2.9f, 0.72f), sign, t);

            // Колесо: тёмный диск, лицевая картинка, колышки между секторами. Крутится вокруг оси Z.
            var pivot = new GameObject("WheelPivot").transform;
            pivot.SetParent(t, false);
            pivot.position = P(0f, 1.75f, 0f);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(pivot, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(2.26f, 0.04f, 2.26f);
            disc.GetComponent<Renderer>().sharedMaterial = woodDark;
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(pivot, false);
            face.transform.localPosition = new Vector3(0f, 0f, -0.045f);
            face.transform.localScale = new Vector3(2.24f, 2.24f, 1f);
            face.GetComponent<Renderer>().sharedMaterial = wheelFace;
            for (int i = 0; i < 12; i++)
            {
                float a = (i * 30f + 15f) * Mathf.Deg2Rad; // по часовой от верха
                var peg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                peg.name = "Peg";
                Object.DestroyImmediate(peg.GetComponent<Collider>());
                peg.transform.SetParent(pivot, false);
                peg.transform.localPosition = new Vector3(Mathf.Sin(a) * 1.0f, Mathf.Cos(a) * 1.0f, -0.09f);
                peg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                peg.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
                peg.GetComponent<Renderer>().sharedMaterial = brass;
            }
            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "HubCap";
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.transform.SetParent(pivot, false);
            cap.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            cap.transform.localScale = new Vector3(0.16f, 0.16f, 0.08f);
            cap.GetComponent<Renderer>().sharedMaterial = brass;

            // Язычок сверху: висит на перекладине, качается от колышков.
            var pointer = new GameObject("PointerPivot").transform;
            pointer.SetParent(t, false);
            pointer.position = P(0f, 3.0f, -0.12f);
            var tongue = ArtKit.Box("Tongue", Vector3.zero, new Vector3(0.2f, 0.2f, 0.06f), red, pointer, false);
            tongue.transform.localPosition = new Vector3(0f, -0.13f, 0f);
            tongue.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tongue.transform.localScale = new Vector3(0.2f, 0.2f, 0.06f);
            tongue.isStatic = false;
            var tongueCap = ArtKit.Box("TongueMount", Vector3.zero, new Vector3(0.12f, 0.1f, 0.12f), brass, pointer, false);
            tongueCap.transform.localPosition = Vector3.zero;
            tongueCap.isStatic = false;

            // Лампочки по столбам и над вывеской.
            var bulbs = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 6; i++)
            {
                foreach (float x in new[] { -1.35f, 1.35f })
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    b.name = "Bulb";
                    Object.DestroyImmediate(b.GetComponent<Collider>());
                    b.transform.SetParent(t, false);
                    b.transform.position = P(x, 0.55f + i * 0.5f, 0.05f);
                    b.transform.localScale = Vector3.one * 0.13f;
                    var r = b.GetComponent<Renderer>();
                    r.sharedMaterial = bulb;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bulbs.Add(r);
                }
            }
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(t, false);
            glow.transform.position = P(0f, 2.4f, -1.4f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.82f, 0.55f);
            glow.range = 6f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;

            // Щит с ковриками для ставок (без коллайдера — коты просто заходят).
            ArtKit.Quad("BetBoard", P(0f, 0.035f, -2.7f), new Vector2(5.1f, 1.7f), mat, t, 0f, 90f);
            var mats = new Transform[3];
            var labels = new TextMesh[3];
            for (int i = 0; i < 3; i++)
            {
                var m = new GameObject($"Mat{i}").transform;
                m.SetParent(t, false);
                m.position = P(-1.7f + i * 1.7f, 0f, -2.7f);
                mats[i] = m;

                var label = ArtKit.Label($"MatLabel{i}", "", P(-1.7f + i * 1.7f, 1.05f, -2.7f), 0f, 0.032f, Color.white, t);
                label.anchor = TextAnchor.LowerCenter;
                label.gameObject.AddComponent<FaceCamera>();
                labels[i] = label;
            }
            var status = ArtKit.Label("Status", "", P(0f, 4.25f, 0.1f), 0f, 0.05f, new Color(1f, 0.85f, 0.35f), t);
            status.gameObject.AddComponent<FaceCamera>();

            var so = new SerializedObject(roulette);
            so.FindProperty("wheel").objectReferenceValue = pivot;
            so.FindProperty("pointer").objectReferenceValue = pointer;
            var matsProperty = so.FindProperty("mats");
            matsProperty.arraySize = 3;
            var labelsProperty = so.FindProperty("matLabels");
            labelsProperty.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                matsProperty.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
                labelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            }
            so.FindProperty("matHalfSize").vector2Value = new Vector2(0.8f, 0.82f);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("bettingSeconds").floatValue = 5f;      // колесо крутится через 5 с после первой ставки
            so.FindProperty("closeBeforeVoteEnd").floatValue = 10f; // за 10 с до игры ставки закрыты
            var bulbsProperty = so.FindProperty("bulbs");
            bulbsProperty.arraySize = bulbs.Count;
            for (int i = 0; i < bulbs.Count; i++) bulbsProperty.GetArrayElementAtIndex(i).objectReferenceValue = bulbs[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            root.transform.SetPositionAndRotation(RoulettePosition, Quaternion.Euler(0f, RouletteYaw, 0f));

            // Двигающиеся части не должны быть статичными (иначе батчинг «запечёт» их на месте).
            foreach (var tr in pivot.GetComponentsInChildren<Transform>(true)) tr.gameObject.isStatic = false;
            foreach (var tr in pointer.GetComponentsInChildren<Transform>(true)) tr.gameObject.isStatic = false;
            foreach (var r in bulbs) r.gameObject.isStatic = false;
        }
    }
}
