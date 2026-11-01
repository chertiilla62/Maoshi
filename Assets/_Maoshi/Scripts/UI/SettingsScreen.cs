using System;
using System.Collections.Generic;
using Maoshi.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Экран настроек с вкладками: Графика / Экран / Картинка / Управление / Звук.
    /// «Общее качество» — пресет: выставляет масштаб, тени, сглаживание, текстуры и свечение разом;
    /// если поменять любой из этих пунктов вручную — пресет показывает «Своё».
    /// Каждое изменение применяется сразу и сохраняется. Открывается из главного меню и из паузы (Esc).
    /// </summary>
    public class SettingsScreen
    {
        public VisualElement Root { get; }

        private readonly List<Selector> _selectors = new();
        private readonly List<(Button tab, VisualElement page)> _tabs = new();
        private Label _fpsNote;

        public SettingsScreen(Action onClose)
        {
            Root = UIKit.Layout("layer", "center", "dim");

            var panel = UIKit.Box("panel", "panel--wide", "settings-panel");
            panel.Add(UIKit.Text("Настройки", "h1"));

            var tabRow = UIKit.Row("tabs");
            panel.Add(tabRow);
            var pages = UIKit.Box("tab-pages");
            panel.Add(pages);

            // ── Графика: то, что влияет на скорость ──
            var graphics = AddTab(tabRow, pages, "Графика");
            AddSelector(graphics, "Общее качество",
                () => GameSettings.PresetName(GameSettings.CurrentPreset),
                GameSettings.StepPreset, "settings-row--main");
            graphics.Add(UIKit.Text("Выставляет все пункты ниже. Поменяешь любой вручную — станет «Своё».", "settings-note", "settings-note--top"));
            AddSection(graphics, "Подробно");
            AddSelector(graphics, "Масштаб рендера",
                () => $"{Mathf.RoundToInt(GraphicsTweaks.RenderScales[GameSettings.RenderScaleIndex] * 100f)}%",
                d => GameSettings.RenderScaleIndex = Clamp(GameSettings.RenderScaleIndex + d, GraphicsTweaks.RenderScales.Length));
            AddSelector(graphics, "Тени",
                () => GraphicsTweaks.ShadowNames[GameSettings.ShadowLevel],
                d => GameSettings.ShadowLevel = Clamp(GameSettings.ShadowLevel + d, GraphicsTweaks.ShadowNames.Length));
            AddSelector(graphics, "Сглаживание",
                () => GraphicsTweaks.MsaaNames[GameSettings.AntiAliasing],
                d => GameSettings.AntiAliasing = Clamp(GameSettings.AntiAliasing + d, GraphicsTweaks.MsaaNames.Length));
            AddSelector(graphics, "Текстуры",
                () => GraphicsTweaks.TextureNames[GameSettings.TextureLevel],
                d => GameSettings.TextureLevel = Clamp(GameSettings.TextureLevel + d, GraphicsTweaks.TextureNames.Length));
            AddSelector(graphics, "Свечение",
                () => OnOff(GameSettings.Bloom),
                d => GameSettings.Bloom = !GameSettings.Bloom);
            graphics.Add(UIKit.Text("Больше всего FPS даёт масштаб рендера ниже 100%, потом тени.", "settings-note"));

            // ── Экран ──
            var screen = AddTab(tabRow, pages, "Экран");
            AddSelector(screen, "Режим экрана",
                () => GameSettings.DisplayName(GameSettings.Display),
                d => GameSettings.Display = GameSettings.Display == GameSettings.DisplayMode.Fullscreen
                    ? GameSettings.DisplayMode.Windowed
                    : GameSettings.DisplayMode.Fullscreen);
            AddSelector(screen, "Разрешение",
                () => GameSettings.ResolutionName(GameSettings.ResolutionIndex),
                // Список идёт от большего к меньшему: «›» — больше, «‹» — меньше.
                d => GameSettings.ResolutionIndex = Clamp(GameSettings.ResolutionIndex - d, GameSettings.Resolutions.Count));
            AddSelector(screen, "Вертикальная синхронизация",
                () => OnOff(GameSettings.VSync),
                d => GameSettings.VSync = !GameSettings.VSync);
            AddSelector(screen, "Ограничение FPS",
                () => GameSettings.VSync ? "Как у монитора" : GameSettings.FpsName(GameSettings.FpsIndex),
                d => { if (!GameSettings.VSync) GameSettings.FpsIndex = Clamp(GameSettings.FpsIndex + d, GameSettings.FpsOptions.Length); });
            _fpsNote = UIKit.Text("", "settings-note");
            screen.Add(_fpsNote);
            AddSelector(screen, "Показывать FPS и пинг",
                () => OnOff(GameSettings.ShowNetStats),
                d => GameSettings.ShowNetStats = !GameSettings.ShowNetStats);
            if (Application.isEditor)
                screen.Add(UIKit.Text("В редакторе Unity режим экрана и разрешение не меняются — только в собранной игре.", "settings-note"));

            // ── Картинка: стиль, на скорость не влияет ──
            var picture = AddTab(tabRow, pages, "Картинка");
            AddSelector(picture, "Контуры",
                () => GraphicsTweaks.OutlineNames[GameSettings.OutlineLevel],
                d => GameSettings.OutlineLevel = Clamp(GameSettings.OutlineLevel + d, GraphicsTweaks.OutlineNames.Length));
            AddSelector(picture, "Постобработка",
                () => OnOff(GameSettings.PostProcessing),
                d => GameSettings.PostProcessing = !GameSettings.PostProcessing);
            AddSelector(picture, "Яркость",
                () => GameSettings.Brightness == 0 ? "0" : GameSettings.Brightness.ToString("+0;−0"),
                d => GameSettings.Brightness = Mathf.Clamp(GameSettings.Brightness + d, -5, 5));
            AddSelector(picture, "Краски",
                () => GraphicsTweaks.SaturationNames[GameSettings.SaturationLevel],
                d => GameSettings.SaturationLevel = Clamp(GameSettings.SaturationLevel + d, GraphicsTweaks.SaturationNames.Length));
            AddSelector(picture, "Поле зрения",
                () => $"{GameSettings.FieldOfView}°",
                d => GameSettings.FieldOfView = Mathf.Clamp(GameSettings.FieldOfView + d * 5, 50, 90));
            picture.Add(UIKit.Text("Яркость, краски и свечение работают при включённой постобработке.", "settings-note"));

            // ── Управление ──
            var input = AddTab(tabRow, pages, "Управление");
            AddSelector(input, "Чувствительность камеры",
                () => $"{GameSettings.MouseSensitivity:0.00}".Replace(',', '.') + "×",
                d => GameSettings.MouseSensitivity = Mathf.Round((GameSettings.MouseSensitivity + d * 0.25f) * 4f) / 4f);
            AddSelector(input, "Тряска камеры",
                () => GameSettings.ReduceMotion ? "Слабая" : "Обычная",
                d => GameSettings.ReduceMotion = !GameSettings.ReduceMotion);
            input.Add(UIKit.Text("Колёсико мыши — приблизить/отдалить камеру, Alt — освободить курсор, F3 — FPS и пинг.", "settings-note"));

            // ── Звук ──
            var sound = AddTab(tabRow, pages, "Звук");
            AddSelector(sound, "Общая громкость",
                () => Percent(GameSettings.Volume),
                d => GameSettings.Volume = Mathf.Round(GameSettings.Volume * 10f + d) / 10f);
            AddSelector(sound, "Музыка",
                () => Percent(GameSettings.MusicVolume),
                d => GameSettings.MusicVolume = Mathf.Round(GameSettings.MusicVolume * 10f + d) / 10f);
            AddSelector(sound, "Звуки",
                () => Percent(GameSettings.SfxVolume),
                d => GameSettings.SfxVolume = Mathf.Round(GameSettings.SfxVolume * 10f + d) / 10f);

            SelectTab(0);

            var bottom = UIKit.Row("settings-bottom");
            bottom.Add(UIKit.Button("Сбросить графику", () =>
            {
                GameSettings.ResetGraphics();
                GameSettings.ApplyAndSave();
                Refresh();
            }, "btn", "btn--ghost", "btn--small"));
            bottom.Add(UIKit.Button("Готово", onClose, "btn", "btn--primary"));
            panel.Add(bottom);

            Root.Add(panel);
            Refresh();
        }

        /// <summary>Перед показом: подтянуть актуальные значения (их могли поменять F3/F4).</summary>
        public void Refresh()
        {
            foreach (var selector in _selectors) selector.Refresh();
            if (_fpsNote != null)
                UIKit.SetText(_fpsNote, GameSettings.VSync
                    ? "Пока включена синхронизация, FPS равен частоте монитора. Выключи её, чтобы выбрать ограничение."
                    : "Без синхронизации возможны «разрывы» картинки, зато меньше задержка.");
        }

        private VisualElement AddTab(VisualElement tabRow, VisualElement pages, string title)
        {
            int index = _tabs.Count;
            var tab = UIKit.Button(title, () => SelectTab(index), "tab");
            var page = new ScrollView(ScrollViewMode.Vertical);
            page.AddToClassList("tab-page");
            page.AddToClassList("settings-scroll");
            tabRow.Add(tab);
            pages.Add(page);
            _tabs.Add((tab, page));
            return page;
        }

        private static void AddSection(VisualElement parent, string title) =>
            parent.Add(UIKit.Text(title, "settings-section"));

        private void SelectTab(int index)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].tab.EnableInClassList("tab--active", i == index);
                UIKit.Show(_tabs[i].page, i == index);
            }
        }

        private void AddSelector(VisualElement parent, string title, Func<string> getText, Action<int> step, string extraClass = null)
        {
            var selector = new Selector(title, getText, direction =>
            {
                step(direction);
                GameSettings.ApplyAndSave();
                // Значения связаны (пресет ↔ пункты, vsync ↔ FPS), поэтому обновляем все строки.
                Refresh();
            });
            if (extraClass != null) selector.AddToClassList(extraClass);
            _selectors.Add(selector);
            parent.Add(selector);
        }

        private static string OnOff(bool value) => value ? "Вкл" : "Выкл";

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        private static int Clamp(int value, int count) => Mathf.Clamp(value, 0, Mathf.Max(0, count - 1));
    }
}
