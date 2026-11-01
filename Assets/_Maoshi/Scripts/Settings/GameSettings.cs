using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maoshi.Settings
{
    /// <summary>
    /// Настройки игры: экран, графика, управление, звук. Хранятся в PlayerPrefs (у каждого игрока свои),
    /// применяются при запуске игры и при каждом изменении в меню настроек.
    /// В редакторе режим окна и разрешение не меняются (это делает только собранная игра) — остальное работает.
    /// </summary>
    public static class GameSettings
    {
        public enum DisplayMode { Fullscreen = 0, Windowed = 1 }

        private const string Prefix = "maoshi.settings.";

        /// <summary>Варианты ограничения FPS (0 — без ограничения). Работает, когда вертикальная синхронизация выключена.</summary>
        public static readonly int[] FpsOptions = { 30, 60, 120, 144, 0 };

        public static DisplayMode Display { get; set; } = DisplayMode.Fullscreen;
        /// <summary>Индекс в списке Resolutions.</summary>
        public static int ResolutionIndex { get; set; }
        /// <summary>Качество текстур: 0 низкое (половина), 1 среднее, 2 высокое (+ анизотропная фильтрация).</summary>
        public static int TextureLevel { get; set; } = 2;
        public static bool VSync { get; set; } = true;
        public static int FpsIndex { get; set; } = 1;

        /// <summary>Тряска камеры при ударах и падениях (можно ослабить, если укачивает).</summary>
        public static bool ReduceMotion { get; set; }

        // Графика (применяет GraphicsTweaks).
        /// <summary>Контуры: 0 выкл, 1 тонкие, 2 обычные, 3 толстые.</summary>
        public static int OutlineLevel { get; set; } = 2;
        /// <summary>Тени: 0 выкл, 1 низкие, 2 средние, 3 высокие.</summary>
        public static int ShadowLevel { get; set; } = 2;
        /// <summary>Сглаживание: 0 выкл, 1 MSAA 2×, 2 MSAA 4×, 3 MSAA 8×.</summary>
        public static int AntiAliasing { get; set; } = 2;
        /// <summary>Индекс в GraphicsTweaks.RenderScales (последний — 100%).</summary>
        public static int RenderScaleIndex { get; set; } = 4;
        public static bool PostProcessing { get; set; } = true;
        public static bool Bloom { get; set; } = true;
        /// <summary>Яркость −5…+5 (шаг ±0.1 экспозиции).</summary>
        public static int Brightness { get; set; }
        /// <summary>Насыщенность красок: индекс в GraphicsTweaks.SaturationOffsets.</summary>
        public static int SaturationLevel { get; set; } = 2;
        /// <summary>Поле зрения камеры, градусы.</summary>
        public static int FieldOfView { get; set; } = 60;
        /// <summary>Окраска своего кота (индекс в CatSkins). −1 — любая свободная.</summary>
        public static int CatSkin { get; set; } = -1;
        /// <summary>Множитель чувствительности камеры (1 — как задумано).</summary>
        public static float MouseSensitivity { get; set; } = 1f;
        /// <summary>Громкость 0–1.</summary>
        public static float Volume { get; set; } = 0.8f;
        /// <summary>Громкость музыки 0–1.</summary>
        public static float MusicVolume { get; set; } = 0.6f;
        /// <summary>Громкость эффектов 0–1.</summary>
        public static float SfxVolume { get; set; } = 0.9f;
        /// <summary>Показывать в углу FPS и пинг.</summary>
        public static bool ShowNetStats { get; set; }

        /// <summary>Что-то поменялось и применено.</summary>
        public static event Action Changed;

        private static List<Vector2Int> _resolutions;

        /// <summary>Разрешения экрана без повторов, от большего к меньшему.</summary>
        public static IReadOnlyList<Vector2Int> Resolutions
        {
            get
            {
                if (_resolutions != null) return _resolutions;

                _resolutions = new List<Vector2Int>();
                foreach (var resolution in Screen.resolutions)
                {
                    var size = new Vector2Int(resolution.width, resolution.height);
                    if (size.x < 800 || size.y < 600 || _resolutions.Contains(size)) continue;
                    _resolutions.Add(size);
                }

                if (_resolutions.Count == 0) _resolutions.Add(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
                _resolutions.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
                return _resolutions;
            }
        }

        // ───────────────────────── Загрузка и сохранение ─────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadOnStartup()
        {
            Load();
            Apply();
        }

        public static void Load()
        {
            Display = (DisplayMode)PlayerPrefs.GetInt(Prefix + "display", (int)DisplayMode.Fullscreen);
            TextureLevel = PlayerPrefs.GetInt(Prefix + "textures", 2);
            VSync = PlayerPrefs.GetInt(Prefix + "vsync", 1) == 1;
            FpsIndex = PlayerPrefs.GetInt(Prefix + "fps", 1);
            ReduceMotion = PlayerPrefs.GetInt(Prefix + "reduceMotion", 0) == 1;
            OutlineLevel = PlayerPrefs.GetInt(Prefix + "outline", 2);
            ShadowLevel = PlayerPrefs.GetInt(Prefix + "shadows", 2);
            AntiAliasing = PlayerPrefs.GetInt(Prefix + "msaa", 2);
            RenderScaleIndex = PlayerPrefs.GetInt(Prefix + "renderscale", 4);
            PostProcessing = PlayerPrefs.GetInt(Prefix + "post", 1) == 1;
            Bloom = PlayerPrefs.GetInt(Prefix + "bloom", 1) == 1;
            Brightness = PlayerPrefs.GetInt(Prefix + "brightness", 0);
            SaturationLevel = PlayerPrefs.GetInt(Prefix + "saturation", 2);
            FieldOfView = PlayerPrefs.GetInt(Prefix + "fov", 60);
            CatSkin = PlayerPrefs.GetInt(Prefix + "catskin", -1);
            MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "sensitivity", 1f);
            Volume = PlayerPrefs.GetFloat(Prefix + "volume", 0.8f);
            MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", 0.6f);
            SfxVolume = PlayerPrefs.GetFloat(Prefix + "sfx", 0.9f);
            ShowNetStats = PlayerPrefs.GetInt(Prefix + "netstats", 0) == 1;

            // Разрешение храним размерами, а не номером: список у разных мониторов разный.
            int width = PlayerPrefs.GetInt(Prefix + "width", Screen.currentResolution.width);
            int height = PlayerPrefs.GetInt(Prefix + "height", Screen.currentResolution.height);
            ResolutionIndex = FindResolution(width, height);

            Clamp();
        }

        public static void Save()
        {
            Clamp();
            var size = Resolutions[ResolutionIndex];
            PlayerPrefs.SetInt(Prefix + "display", (int)Display);
            PlayerPrefs.SetInt(Prefix + "width", size.x);
            PlayerPrefs.SetInt(Prefix + "height", size.y);
            PlayerPrefs.SetInt(Prefix + "textures", TextureLevel);
            PlayerPrefs.SetInt(Prefix + "vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fps", FpsIndex);
            PlayerPrefs.SetInt(Prefix + "reduceMotion", ReduceMotion ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "outline", OutlineLevel);
            PlayerPrefs.SetInt(Prefix + "shadows", ShadowLevel);
            PlayerPrefs.SetInt(Prefix + "msaa", AntiAliasing);
            PlayerPrefs.SetInt(Prefix + "renderscale", RenderScaleIndex);
            PlayerPrefs.SetInt(Prefix + "post", PostProcessing ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "bloom", Bloom ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "brightness", Brightness);
            PlayerPrefs.SetInt(Prefix + "saturation", SaturationLevel);
            PlayerPrefs.SetInt(Prefix + "fov", FieldOfView);
            PlayerPrefs.SetInt(Prefix + "catskin", CatSkin);
            PlayerPrefs.SetFloat(Prefix + "sensitivity", MouseSensitivity);
            PlayerPrefs.SetFloat(Prefix + "volume", Volume);
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "sfx", SfxVolume);
            PlayerPrefs.SetInt(Prefix + "netstats", ShowNetStats ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Применить всё и сохранить.</summary>
        public static void ApplyAndSave()
        {
            Apply();
            Save();
        }

        public static void Apply()
        {
            Clamp();

            // Экран. В окне — выбранный размер; в полноэкранном — выбранное разрешение без рамок (быстрое переключение Alt+Tab).
            var size = Resolutions[ResolutionIndex];
            var mode = Display == DisplayMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (!Application.isEditor && (Screen.fullScreenMode != mode || Screen.width != size.x || Screen.height != size.y))
                Screen.SetResolution(size.x, size.y, mode);

            // Уровень качества Unity всегда один — «PC» (в нём рендер со всеми эффектами: контуры, мультяшный свет).
            // Всё, что зависит от качества, задают пункты графики (GraphicsTweaks) и пресет «Общее качество».
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            if (pc >= 0 && QualitySettings.GetQualityLevel() != pc)
                QualitySettings.SetQualityLevel(pc, true);

            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : (FpsOptions[FpsIndex] > 0 ? FpsOptions[FpsIndex] : -1);

            AudioListener.volume = Volume;
            GraphicsTweaks.Apply();

            Changed?.Invoke();
        }

        // ───────────────────────── Подписи для меню ─────────────────────────

        public static string DisplayName(DisplayMode mode) => mode == DisplayMode.Windowed ? "В окне" : "Полный экран";

        public static string ResolutionName(int index)
        {
            var size = Resolutions[Mathf.Clamp(index, 0, Resolutions.Count - 1)];
            return $"{size.x} × {size.y}";
        }

        public static string FpsName(int index) => FpsOptions[index] > 0 ? FpsOptions[index].ToString() : "Без ограничения";

        // ───────────────────────── Вспомогательное ─────────────────────────

        private static int FindResolution(int width, int height)
        {
            var list = Resolutions;
            for (int i = 0; i < list.Count; i++)
                if (list[i].x == width && list[i].y == height) return i;

            // Нет точного — берём самое близкое по площади.
            int best = 0;
            long bestDiff = long.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                long diff = Math.Abs((long)list[i].x * list[i].y - (long)width * height);
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }

            return best;
        }

        private static void Clamp()
        {
            ResolutionIndex = Mathf.Clamp(ResolutionIndex, 0, Resolutions.Count - 1);
            TextureLevel = Mathf.Clamp(TextureLevel, 0, GraphicsTweaks.TextureNames.Length - 1);
            FpsIndex = Mathf.Clamp(FpsIndex, 0, FpsOptions.Length - 1);
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.25f, 3f);
            Volume = Mathf.Clamp01(Volume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            OutlineLevel = Mathf.Clamp(OutlineLevel, 0, GraphicsTweaks.OutlineScales.Length - 1);
            ShadowLevel = Mathf.Clamp(ShadowLevel, 0, GraphicsTweaks.ShadowDistances.Length - 1);
            AntiAliasing = Mathf.Clamp(AntiAliasing, 0, GraphicsTweaks.MsaaSamples.Length - 1);
            RenderScaleIndex = Mathf.Clamp(RenderScaleIndex, 0, GraphicsTweaks.RenderScales.Length - 1);
            Brightness = Mathf.Clamp(Brightness, -5, 5);
            SaturationLevel = Mathf.Clamp(SaturationLevel, 0, GraphicsTweaks.SaturationOffsets.Length - 1);
            FieldOfView = Mathf.Clamp(FieldOfView, 50, 90);
        }

        // ───────────────────────── Пресеты качества ─────────────────────────

        /// <summary>Пресеты «Общего качества». Последний вариант в меню — «Своё» (пункты поменяны вручную).</summary>
        public static readonly string[] PresetNames = { "Низкое", "Среднее", "Высокое", "Ультра" };
        public const int DefaultPreset = 2;

        private struct Preset
        {
            public int RenderScale, Shadows, Aa, Textures;
            public bool Bloom;
        }

        // Масштаб — индекс в GraphicsTweaks.RenderScales { 50, 67, 75, 85, 100 }.
        private static readonly Preset[] Presets =
        {
            new() { RenderScale = 2, Shadows = 1, Aa = 0, Textures = 0, Bloom = false }, // Низкое
            new() { RenderScale = 3, Shadows = 2, Aa = 1, Textures = 1, Bloom = true },  // Среднее
            new() { RenderScale = 4, Shadows = 2, Aa = 2, Textures = 2, Bloom = true },  // Высокое
            new() { RenderScale = 4, Shadows = 3, Aa = 3, Textures = 2, Bloom = true },  // Ультра
        };

        /// <summary>Какой пресет совпадает с текущими пунктами; −1 — «Своё».</summary>
        public static int CurrentPreset
        {
            get
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    var p = Presets[i];
                    if (p.RenderScale == RenderScaleIndex && p.Shadows == ShadowLevel && p.Aa == AntiAliasing
                        && p.Textures == TextureLevel && p.Bloom == Bloom)
                        return i;
                }
                return -1;
            }
        }

        public static string PresetName(int preset) => preset >= 0 && preset < PresetNames.Length ? PresetNames[preset] : "Своё";

        /// <summary>Выставить все пункты графики по пресету (стиль картинки — контуры, яркость, краски — не трогает).</summary>
        public static void ApplyPreset(int preset)
        {
            var p = Presets[Mathf.Clamp(preset, 0, Presets.Length - 1)];
            RenderScaleIndex = p.RenderScale;
            ShadowLevel = p.Shadows;
            AntiAliasing = p.Aa;
            TextureLevel = p.Textures;
            Bloom = p.Bloom;
        }

        /// <summary>Шаг пресета стрелкой: из «Своё» ‹ — Высокое, › — Ультра; дальше по кругу нет — упор в края.</summary>
        public static void StepPreset(int direction)
        {
            int current = CurrentPreset;
            int next = current < 0
                ? (direction < 0 ? DefaultPreset : DefaultPreset + 1)
                : Mathf.Clamp(current + direction, 0, Presets.Length - 1);
            ApplyPreset(next);
        }

        /// <summary>Вернуть графику и картинку к значениям по умолчанию (кнопка в настройках).</summary>
        public static void ResetGraphics()
        {
            ApplyPreset(DefaultPreset);
            OutlineLevel = 2;
            PostProcessing = true;
            Brightness = 0;
            SaturationLevel = 2;
            FieldOfView = 60;
        }
    }
}
