using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 18: мультяшная графика.
    /// - Все материалы проекта и моделей Kenney переводятся на шейдер Maoshi/Toon (ступенчатый свет, цветная тень, ободок).
    /// - Кот: мультяшные материалы тела для всех пород (текстура AnimX, постеризация), мех выключен по умолчанию.
    /// - Экранные контуры (Maoshi/Outline) подключаются к рендерерам URP.
    /// - Постобработка: насыщенность, контраст, лёгкое свечение, виньетка. Мультяшное небо.
    /// Запуск: Maoshi → Setup → Stage 18 - Cartoon Look. Потом Stage 16 и Stage 17 (свет и небо в сценах).
    /// </summary>
    public static class MaoshiSetupStage18
    {
        public const string ToonShader = "Maoshi/Toon";
        private const string OutlineMaterialPath = ArtKit.MaterialsFolder + "/M_Outline.mat";
        private const string CatToonFolder = ArtKit.MaterialsFolder + "/CatToon";
        private const string FeatureName = "MaoshiOutline";

        [MenuItem("Maoshi/Setup/Stage 18 - Cartoon Look")]
        public static void Setup()
        {
            var toon = Shader.Find(ToonShader);
            if (toon == null)
            {
                Debug.LogError("[Maoshi Setup] Шейдер Maoshi/Toon не найден (ошибка компиляции?).");
                return;
            }

            int converted = ConvertProjectMaterials(toon);
            AssetDatabase.SaveAssets();
            // Встроенные материалы моделей Kenney переводит KenneyImportSettings при импорте.
            AssetDatabase.ImportAsset(KenneyImportSettings.Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            SetupCat(toon);
            SetupOutline();
            SetupPostProcessing();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maoshi Setup] Этап 18 готов: мультяшный вид (материалов переведено: {converted}). Запусти Stage 16 и Stage 17.");
        }

        // ───────────────────────── Материалы ─────────────────────────

        private static int ConvertProjectMaterials(Shader toon)
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Maoshi" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(CatToonFolder) || path == OutlineMaterialPath || path.Contains("/UI/")) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || !IsLit(material.shader)) continue;
                ConvertMaterial(material, toon);
                count++;
            }
            return count;
        }

        public static bool IsLit(Shader shader) =>
            shader != null && (shader.name == "Universal Render Pipeline/Lit" || shader.name == "Universal Render Pipeline/Simple Lit" || shader.name == "Standard");

        /// <summary>Сменить шейдер, сохранив цвет, текстуру и свечение.</summary>
        public static void ConvertMaterial(Material material, Shader toon)
        {
            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            Vector2 scale = material.HasProperty("_BaseMap") ? material.GetTextureScale("_BaseMap") : Vector2.one;
            Color emission = material.HasProperty("_EmissionColor") && material.IsKeywordEnabled("_EMISSION") ? material.GetColor("_EmissionColor") : Color.black;

            material.shader = toon;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(material);
        }

        // ───────────────────────── Кот ─────────────────────────

        private static void SetupCat(Shader toon)
        {
            MaoshiSetup.EnsureFolder(CatToonFolder);
            var prefabRoot = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                var appearance = prefabRoot.GetComponent<CatAppearance>();
                var map = new Dictionary<Material, Material>();

                Material Toon(Material source)
                {
                    if (source == null) return null;
                    if (source.shader == toon) return source;
                    if (map.TryGetValue(source, out var cached)) return cached;
                    string path = $"{CatToonFolder}/{source.name}_Toon.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(toon);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = toon;
                    material.name = source.name + "_Toon"; // имя начинается как у AnimX — CatAppearance найдёт слот
                    Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                    material.SetTexture("_BaseMap", texture);
                    material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_Posterize", 7f);
                    material.SetFloat("_Saturation", 1.25f);
                    material.SetFloat("_RampThreshold", 0.45f);
                    material.SetColor("_ShadeColor", new Color(0.62f, 0.58f, 0.78f));
                    material.SetColor("_RimColor", new Color(1f, 0.95f, 0.85f, 0.5f));
                    EditorUtility.SetDirty(material);
                    map[source] = material;
                    return material;
                }

                if (appearance != null)
                {
                    var so = new SerializedObject(appearance);
                    var breeds = so.FindProperty("breeds");
                    for (int i = 0; i < breeds.arraySize; i++)
                    {
                        var body = breeds.GetArrayElementAtIndex(i).FindPropertyRelative("body");
                        body.objectReferenceValue = Toon(body.objectReferenceValue as Material);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                foreach (var renderer in prefabRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null || !materials[i].name.StartsWith("M_Cat.body")) continue;
                        materials[i] = Toon(materials[i]);
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, MaoshiSetupStage2.CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        // ───────────────────────── Контуры ─────────────────────────

        public static void SetupOutline()
        {
            var shader = Shader.Find("Maoshi/Outline");
            if (shader == null)
            {
                Debug.LogError("[Maoshi Setup] Шейдер Maoshi/Outline не найден.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            }
            material.shader = shader;
            // Значения под устойчивый к ряби вариант шейдера (силуэт по второй производной 1/z, изломы только вблизи).
            material.SetFloat("_DepthThreshold", 0.12f);
            material.SetFloat("_Thickness", 1.2f);
            material.SetFloat("_NormalThreshold", 0.9f);
            material.SetFloat("_NormalFadeStart", 10f);
            material.SetFloat("_NormalFadeEnd", 28f);
            material.SetFloat("_FadeStart", 40f);
            material.SetFloat("_FadeEnd", 120f);
            EditorUtility.SetDirty(material);

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Settings")) continue;
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                FullScreenPassRendererFeature feature = null;
                foreach (var existing in data.rendererFeatures)
                    if (existing is FullScreenPassRendererFeature f && existing.name == FeatureName) feature = f;

                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    feature.name = FeatureName;
                    AssetDatabase.AddObjectToAsset(feature, data);
                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
                    {
                        map.arraySize++;
                        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                feature.passMaterial = material;
                feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
                feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color;
                feature.fetchColorBuffer = true;
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
                Debug.Log($"[Maoshi Setup] Контуры подключены к {path}");
            }
        }

        // ───────────────────────── Постобработка ─────────────────────────

        private static void SetupPostProcessing()
        {
            VolumeProfile profile = null;
            var settings = GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>();
            if (settings != null) profile = settings.volumeProfile;
            if (profile == null) profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset");
            if (profile == null)
            {
                Debug.LogWarning("[Maoshi Setup] Не найден профиль постобработки по умолчанию.");
                return;
            }

            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T component))
                {
                    component = profile.Add<T>(true);
                    if (EditorUtility.IsPersistent(profile)) AssetDatabase.AddObjectToAsset(component, profile);
                }
                component.active = true;
                return component;
            }

            var color = Get<ColorAdjustments>();
            color.saturation.Override(14f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.12f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));

            var tonemapping = Get<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var bloom = Get<Bloom>();
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);

            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            Debug.Log("[Maoshi Setup] Постобработка: " + AssetDatabase.GetAssetPath(profile));
        }
    }
}
