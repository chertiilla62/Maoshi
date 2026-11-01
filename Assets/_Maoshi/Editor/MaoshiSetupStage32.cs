using Maoshi.EditorTools.Art;
using Maoshi.MiniGames.YarnRam;
using Maoshi.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 32: «Клубок-таран» → «Клубки в корзинку». На крыше этапа 13 по краю ставятся 8 корзинок
    /// (по одной на игрока, лишние прячутся), последний клубок становится золотым (3 очка),
    /// правила — очки вместо выбывания, препятствия (труба, вентиляция, окно) убраны. Запуск: Maoshi → Setup → Stage 32 - Yarn Baskets (повторный — пересобирает корзинки).
    /// После пересборки сцены этапами 13/17 запустить снова.
    /// </summary>
    public static class MaoshiSetupStage32
    {
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/YarnRam.unity";
        private const float RoofTop = 8.1f;

        [MenuItem("Maoshi/Setup/Stage 32 - Yarn Baskets")]
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

            var definition = AssetDatabase.LoadAssetAtPath<MiniGameDefinition>(MaoshiSetupStage3.MiniGamesFolder + "/YarnRam/YarnRam.asset");
            if (definition != null)
            {
                definition.EditorSetup("yarn_ram", "Клубки в корзинку",
                    "Пинай гигантские клубки (ЛКМ / F) в свою корзинку цвета твоего кота: клубок — очко, золотой — 3. Сбивай соперников клубками, загораживай их корзинки. Упал с крыши — минус очко.",
                    "YarnRam", new[] { 10, 7, 5, 4, 3, 2, 1, 1 });
                EditorUtility.SetDirty(definition);
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("Baskets");
            if (old != null) Object.DestroyImmediate(old);

            // Препятствия на крыше мешают катать клубки — убираем: крыша чистая.
            foreach (var name in new[] { "Chimney", "Vent", "Skylight" })
            {
                var obstacle = GameObject.Find(name);
                if (obstacle != null) Object.DestroyImmediate(obstacle);
            }

            var group = ArtKit.Group("Baskets");
            var wicker = ArtKit.Color("BasketWicker", Color.white, 0.15f);
            wicker.EnableKeyword("_EMISSION");
            wicker.SetColor("_EmissionColor", Color.black);
            wicker.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(wicker);
            var bottom = ArtKit.Color("BasketBottom", new Color(0.45f, 0.30f, 0.17f));

            // Углы и середины сторон, чуть внутрь от края (крыша 14×14, клубок радиусом 0.8).
            Vector2[] spots =
            {
                new(-5.4f, -5.4f), new(5.4f, 5.4f), new(5.4f, -5.4f), new(-5.4f, 5.4f),
                new(0f, -5.6f), new(0f, 5.6f), new(-5.6f, 0f), new(5.6f, 0f),
            };
            var baskets = new YarnBasket[spots.Length];
            for (int i = 0; i < spots.Length; i++)
                baskets[i] = Basket(group, i, new Vector3(spots[i].x, RoofTop, spots[i].y), wicker, bottom);

            // Последний клубок — золотой, 3 очка.
            var mode = Object.FindFirstObjectByType<YarnRamMode>();
            if (mode != null)
            {
                var so = new SerializedObject(mode);
                var ballsProperty = so.FindProperty("balls");
                if (ballsProperty.arraySize > 0)
                {
                    var gold = ballsProperty.GetArrayElementAtIndex(ballsProperty.arraySize - 1).objectReferenceValue as YarnBall;
                    if (gold != null)
                    {
                        var goldMat = ArtKit.Color("YarnGold", new Color(1f, 0.8f, 0.2f), 0.6f);
                        goldMat.EnableKeyword("_EMISSION");
                        goldMat.SetColor("_EmissionColor", new Color(0.45f, 0.32f, 0.05f));
                        EditorUtility.SetDirty(goldMat);
                        foreach (var r in gold.GetComponentsInChildren<Renderer>()) r.sharedMaterial = goldMat;
                        var ballSo = new SerializedObject(gold);
                        ballSo.FindProperty("points").intValue = 3;
                        ballSo.FindProperty("minRamSpeed").floatValue = 3f;
                        ballSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                    for (int i = 0; i < ballsProperty.arraySize - 1; i++)
                    {
                        if (ballsProperty.GetArrayElementAtIndex(i).objectReferenceValue is not YarnBall ball) continue;
                        var ballSo = new SerializedObject(ball);
                        ballSo.FindProperty("points").intValue = 1;
                        ballSo.FindProperty("minRamSpeed").floatValue = 3f;
                        ballSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                var basketsProperty = so.FindProperty("baskets");
                basketsProperty.arraySize = baskets.Length;
                for (int i = 0; i < baskets.Length; i++) basketsProperty.GetArrayElementAtIndex(i).objectReferenceValue = baskets[i];
                so.FindProperty("endWhenOnePlayerLeft").boolValue = false;
                so.FindProperty("timeLimitSeconds").floatValue = 90f;
                so.FindProperty("startBalls").intValue = 3;
                so.FindProperty("extraBallInterval").floatValue = 18f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 32 готов: «Клубки в корзинку».");
        }

        /// <summary>Корзинка: тёмное дно, плетёный бортик из 14 прутьев (без коллайдеров), табличка с именем и счётом.</summary>
        private static YarnBasket Basket(Transform parent, int index, Vector3 at, Material wicker, Material bottom)
        {
            var root = new GameObject($"Basket{index}");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "BasketBottom";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            floor.transform.localScale = new Vector3(2.3f, 0.01f, 2.3f);
            floor.GetComponent<Renderer>().sharedMaterial = bottom;

            var tinted = new System.Collections.Generic.List<Renderer>();
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BasketRim";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(2.55f, 0.012f, 2.55f);
            ring.GetComponent<Renderer>().sharedMaterial = wicker;
            tinted.Add(ring.GetComponent<Renderer>());
            floor.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            const int rods = 14;
            for (int k = 0; k < rods; k++)
            {
                float a = k * Mathf.PI * 2f / rods;
                var rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rod.name = "Rod";
                Object.DestroyImmediate(rod.GetComponent<Collider>());
                rod.transform.SetParent(root.transform, false);
                rod.transform.localPosition = new Vector3(Mathf.Cos(a) * 1.2f, 0.2f, Mathf.Sin(a) * 1.2f);
                rod.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                rod.transform.localScale = new Vector3(0.1f, 0.4f, 0.45f);
                rod.GetComponent<Renderer>().sharedMaterial = wicker;
                tinted.Add(rod.GetComponent<Renderer>());
            }

            var label = ArtKit.Label("BasketLabel", "", at + Vector3.up * 1.9f, 0f, 0.04f, Color.white, root.transform);
            label.gameObject.AddComponent<Maoshi.Hub.FaceCamera>();
            label.fontStyle = FontStyle.Bold;

            var basket = root.AddComponent<YarnBasket>();
            var so = new SerializedObject(basket);
            var tintedProperty = so.FindProperty("tinted");
            tintedProperty.arraySize = tinted.Count;
            for (int k = 0; k < tinted.Count; k++) tintedProperty.GetArrayElementAtIndex(k).objectReferenceValue = tinted[k];
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("radius").floatValue = 1.05f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return basket;
        }
    }
}
