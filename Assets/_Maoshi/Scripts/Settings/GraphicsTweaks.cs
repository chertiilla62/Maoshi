using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Maoshi.Settings
{
    /// <summary>
    /// Применяет графические настройки из GameSettings к рендеру:
    /// контуры, тени, сглаживание, масштаб рендера, постобработка, свечение, яркость, насыщенность, поле зрения.
    ///
    /// - Контуры: глобальная переменная шейдера _MaoshiOutlineScale (материал контура не трогаем).
    /// - Тени, сглаживание, масштаб: поля текущего ассета URP. В редакторе исходные значения ассетов
    ///   запоминаются и возвращаются при выходе из Play, чтобы настройки игрока не попадали в проект.
    /// - Яркость, насыщенность, свечение: свой глобальный Volume поверх профиля по умолчанию (создаётся в памяти).
    /// - Постобработка и поле зрения: у главной камеры, заново при каждой загрузке сцены.
    /// </summary>
    public static class GraphicsTweaks
    {
        // Базовые значения профиля по умолчанию (Stage 18) — свой Volume задаёт их со сдвигом от настроек.
        private const float BaseSaturation = 14f;
        private const float BaseExposure = 0.12f;
        private const float BaseBloom = 0.45f;

        public static readonly float[] OutlineScales = { -1f, 0.75f, 1f, 1.6f };
        public static readonly string[] OutlineNames = { "Выкл", "Тонкие", "Обычные", "Толстые" };

        public static readonly float[] ShadowDistances = { 0f, 30f, 60f, 100f };
        public static readonly int[] ShadowCascades = { 1, 1, 2, 4 };
        public static readonly int[] ShadowResolutions = { 512, 1024, 2048, 4096 };
        public static readonly string[] ShadowNames = { "Выкл", "Низкие", "Средние", "Высокие" };

        public static readonly string[] TextureNames = { "Низкие", "Средние", "Высокие" };

        public static readonly int[] MsaaSamples = { 1, 2, 4, 8 };
        public static readonly string[] MsaaNames = { "Выкл", "MSAA 2×", "MSAA 4×", "MSAA 8×" };

        public static readonly float[] RenderScales = { 0.5f, 0.67f, 0.75f, 0.85f, 1f };

        public static readonly float[] SaturationOffsets = { -14f, -6f, 0f, 12f };
        public static readonly string[] SaturationNames = { "Натуральные", "Мягкие", "Обычные", "Сочные" };

        private static readonly int OutlineScaleId = Shader.PropertyToID("_MaoshiOutlineScale");

        private static Volume _volume;
        private static ColorAdjustments _color;
        private static Bloom _bloom;
        private static bool _hooked;

#if UNITY_EDITOR
        private struct AssetBackup
        {
            public float ShadowDistance, RenderScale;
            public int Cascades, Msaa, ShadowRes, ExtraShadowRes;
            public UpscalingFilterSelection Upscaling;
        }
        private static readonly Dictionary<UniversalRenderPipelineAsset, AssetBackup> Backups = new();
#endif

        public static void Apply()
        {
            Hook();

            Shader.SetGlobalFloat(OutlineScaleId, OutlineScales[GameSettings.OutlineLevel]);

            var asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                        ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                Backup(asset);
                asset.shadowDistance = ShadowDistances[GameSettings.ShadowLevel];
                asset.shadowCascadeCount = ShadowCascades[GameSettings.ShadowLevel];
                asset.msaaSampleCount = MsaaSamples[GameSettings.AntiAliasing];
                float scale = RenderScales[GameSettings.RenderScaleIndex];
                asset.renderScale = scale;
                // Ниже 100% — растягиваем через FSR (резче обычного билинейного).
                asset.upscalingFilter = scale < 0.99f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
                int resolution = ShadowResolutions[GameSettings.ShadowLevel];
                asset.mainLightShadowmapResolution = resolution;
                asset.additionalLightsShadowmapResolution = resolution;
            }

            // Текстуры: низкие — половинный размер (мип 1), высокие — с анизотропной фильтрацией.
            QualitySettings.globalTextureMipmapLimit = GameSettings.TextureLevel == 0 ? 1 : 0;
            QualitySettings.anisotropicFiltering = GameSettings.TextureLevel == 2
                ? AnisotropicFiltering.ForceEnable
                : AnisotropicFiltering.Enable;

            ApplyVolume();
            ApplyScene();
        }

        /// <summary>То, что живёт в сцене: камера и солнце. Вызывается и после каждой загрузки сцены.</summary>
        private static void ApplyScene()
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null) continue;
                if (camera.TryGetComponent(out UniversalAdditionalCameraData data))
                    data.renderPostProcessing = GameSettings.PostProcessing;
                if (camera == Camera.main) camera.fieldOfView = GameSettings.FieldOfView;
            }

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                if (GameSettings.ShadowLevel == 0) light.shadows = LightShadows.None;
                else if (light.shadows == LightShadows.None) light.shadows = LightShadows.Soft;
            }
        }

        private static void ApplyVolume()
        {
            if (_volume == null)
            {
                var go = new GameObject("MaoshiGraphicsVolume");
                Object.DontDestroyOnLoad(go);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 100f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _color = profile.Add<ColorAdjustments>();
                _bloom = profile.Add<Bloom>();
                _volume.sharedProfile = profile;
            }

            _color.postExposure.Override(BaseExposure + GameSettings.Brightness * 0.1f);
            _color.saturation.Override(BaseSaturation + SaturationOffsets[GameSettings.SaturationLevel]);
            _bloom.intensity.Override(GameSettings.Bloom ? BaseBloom : 0f);
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            SceneManager.sceneLoaded += (_, _) => ApplyScene();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
                foreach (var pair in Backups)
                {
                    if (pair.Key == null) continue;
                    pair.Key.shadowDistance = pair.Value.ShadowDistance;
                    pair.Key.shadowCascadeCount = pair.Value.Cascades;
                    pair.Key.msaaSampleCount = pair.Value.Msaa;
                    pair.Key.renderScale = pair.Value.RenderScale;
                    pair.Key.upscalingFilter = pair.Value.Upscaling;
                    pair.Key.mainLightShadowmapResolution = pair.Value.ShadowRes;
                    pair.Key.additionalLightsShadowmapResolution = pair.Value.ExtraShadowRes;
                }
                Backups.Clear();
                Shader.SetGlobalFloat(OutlineScaleId, 0f);
                QualitySettings.globalTextureMipmapLimit = 0;
            };
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _volume = null;
            _color = null;
            _bloom = null;
            _hooked = false;
        }

        private static void Backup(UniversalRenderPipelineAsset asset)
        {
#if UNITY_EDITOR
            if (Backups.ContainsKey(asset)) return;
            Backups[asset] = new AssetBackup
            {
                ShadowDistance = asset.shadowDistance,
                Cascades = asset.shadowCascadeCount,
                Msaa = asset.msaaSampleCount,
                RenderScale = asset.renderScale,
                Upscaling = asset.upscalingFilter,
                ShadowRes = asset.mainLightShadowmapResolution,
                ExtraShadowRes = asset.additionalLightsShadowmapResolution
            };
#endif
        }
    }
}
