using Maoshi.Cat;
using Maoshi.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Весь интерфейс игры на UI Toolkit. Живёт на объекте NetworkManager (не уничтожается между сценами).
    /// Решает, какой экран показать: главное меню → подключение → лобби → игровой HUD.
    /// Поверх всего — пауза (Esc) и настройки.
    /// Пока открыта пауза или настройки, кот не слушается (CatInput.Blocked).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MaoshiUI : MonoBehaviour
    {
        [SerializeField] private StyleSheet styleSheet;
        [Tooltip("Шрифты системы по порядку: первый найденный станет шрифтом интерфейса (нужна кириллица).")]
        [SerializeField] private string[] preferredFonts = { "Segoe UI", "Trebuchet MS", "Arial" };

        private UIDocument _document;
        private VisualElement _root;

        private MainMenuScreen _mainMenu;
        private ConnectingScreen _connecting;
        private LobbyScreen _lobby;
        private HudScreen _hud;
        private SettingsScreen _settings;
        private ShopScreen _shop;
        private bool _shopOpen;
        private VisualElement _pause;
        private DevConsoleView _console;
        private InviteToast _inviteToast;

        private bool _pauseOpen;
        private bool _settingsOpen;

        private void Start()
        {
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[Maoshi UI] У UIDocument нет панели. Запусти Maoshi → Setup → Stage 11.");
                enabled = false;
                return;
            }

            if (styleSheet != null) _root.styleSheets.Add(styleSheet);
            _root.pickingMode = PickingMode.Ignore;
            _root.AddToClassList("maoshi-root");
            // Шрифты дизайна (Dela Gothic One, Nunito, Neucha), если они положены в проект: стили собирает Stage 20.
            var fonts = Resources.Load<StyleSheet>("Maoshi/MaoshiFonts");
            if (fonts != null) _root.styleSheets.Add(fonts);
            else ApplyFont();

            HookUISounds();

            _mainMenu = new MainMenuScreen(OpenSettings, OpenShop);
            _shop = new ShopScreen(CloseShop);
            _connecting = new ConnectingScreen();
            _lobby = new LobbyScreen();
            _hud = new HudScreen();
            _settings = new SettingsScreen(CloseSettings);
            _pause = BuildPause();

            _root.Add(_mainMenu.Root);
            _root.Add(_connecting.Root);
            _root.Add(_lobby.Root);
            _root.Add(_hud.Root);
            _root.Add(_pause);
            _root.Add(_shop.Root);
            _root.Add(_settings.Root);
            _inviteToast = new InviteToast();
            _root.Add(_inviteToast.Root);
            _console = new DevConsoleView();
            _root.Add(_console.Root);

            HideAll();
        }

        private void Update()
        {
            if (_root == null) return;

            var connection = ConnectionManager.Instance;
            var state = connection != null ? connection.State : ConnectionState.Offline;
            bool inMenu = state == ConnectionState.Connected && connection.IsInMenu;
            bool inGame = state == ConnectionState.Connected && !connection.IsInMenu;

            // Консоль разработчика поверх всего; Esc, пока она открыта, закрывает её, а не паузу.
            _inviteToast.Tick();
            bool consoleWasOpen = Maoshi.DevConsole.IsOpen;
            _console.Tick();
            if (!consoleWasOpen) HandleEscape(inGame);
            if (!inGame) _pauseOpen = false;

            if (state != ConnectionState.Offline && _shopOpen) CloseShop();
            UIKit.Show(_mainMenu.Root, state == ConnectionState.Offline && !_shopOpen);
            UIKit.Show(_shop.Root, state == ConnectionState.Offline && _shopOpen);
            UIKit.Show(_connecting.Root, state == ConnectionState.Connecting);
            UIKit.Show(_lobby.Root, inMenu);
            UIKit.Show(_hud.Root, inGame);
            UIKit.Show(_pause, _pauseOpen && !_settingsOpen);
            UIKit.Show(_settings.Root, _settingsOpen);

            if (state == ConnectionState.Offline && _shopOpen) _shop.Refresh();
            else if (state == ConnectionState.Offline) _mainMenu.Refresh(connection);
            else if (state == ConnectionState.Connecting) _connecting.Refresh(connection);
            else if (inMenu) _lobby.Refresh(connection);
            else if (inGame) _hud.Refresh();

            // Кот не двигается, пока открыто меню; клик по кнопкам — не толчок.
            CatInput.Blocked = _pauseOpen || _settingsOpen || Maoshi.DevConsole.IsOpen || Maoshi.Network.SteamService.OverlayOpen;
            CatInput.PointerOverUI = IsPointerOverUI();
        }

        private void OnDisable()
        {
            CatInput.Blocked = false;
            Maoshi.DevConsole.IsOpen = false;
            _inviteToast?.Dispose();
            CatInput.PointerOverUI = false;
        }

        // ───────────────────────── Пауза и настройки ─────────────────────────

        private void HandleEscape(bool inGame)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            if (_settingsOpen) CloseSettings();
            else if (_shopOpen) CloseShop();
            else if (inGame) _pauseOpen = !_pauseOpen;
        }

        private VisualElement BuildPause()
        {
            var layer = UIKit.Layout("layer", "center", "dim");
            var panel = UIKit.Box("panel", "panel--narrow");
            panel.Add(UIKit.Text("Пауза", "h1"));
            panel.Add(UIKit.Text("Игра у остальных идёт дальше — твой кот просто стоит.", "muted"));
            panel.Add(UIKit.Spacer());
            panel.Add(UIKit.Button("Продолжить", () => _pauseOpen = false, "btn--primary"));
            panel.Add(UIKit.Button("Настройки", OpenSettings));
            panel.Add(UIKit.Button("Выйти в меню", () =>
            {
                _pauseOpen = false;
                ConnectionManager.Instance?.Leave();
            }, "btn--ghost"));
            layer.Add(panel);
            return layer;
        }

        private void OpenSettings()
        {
            _settings.Refresh();
            _settingsOpen = true;
        }

        private void CloseSettings() => _settingsOpen = false;

        private void OpenShop()
        {
            _shop.Open();
            _shopOpen = true;
        }

        private void CloseShop()
        {
            _shopOpen = false;
            _shop.OnClosed();
        }

        private void HideAll()
        {
            foreach (var child in _root.Children()) UIKit.Show(child, false);
        }

        // ───────────────────────── Звуки интерфейса ─────────────────────────

        private Button _hovered;

        /// <summary>Щелчок и лёгкий «тик» при наведении — для всех кнопок сразу (события всплывают до корня).</summary>
        private void HookUISounds()
        {
            _root.RegisterCallback<ClickEvent>(e =>
            {
                var button = FindButton(e.target as VisualElement);
                if (button != null && button.enabledInHierarchy) Maoshi.Audio.AudioManager.Play("ui_click", 0.7f);
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerOverEvent>(e =>
            {
                var button = FindButton(e.target as VisualElement);
                if (button == _hovered) return;
                _hovered = button;
                if (button != null && button.enabledInHierarchy) Maoshi.Audio.AudioManager.Play("ui_hover", 0.35f, 0.05f);
            }, TrickleDown.TrickleDown);
        }

        private static Button FindButton(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e is Button b) return b;
            return null;
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        /// <summary>Мышь над панелью или кнопкой (а не над пустым местом поверх игры).</summary>
        private bool IsPointerOverUI()
        {
            var mouse = Mouse.current;
            var panel = _root.panel;
            if (mouse == null || panel == null) return false;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(panelPosition);
            return picked != null && picked != _root;
        }

        /// <summary>Системный шрифт с кириллицей: встроенный шрифт UI Toolkit может её не содержать.</summary>
        private void ApplyFont()
        {
            Font font = null;
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                foreach (var name in preferredFonts)
                {
                    if (System.Array.IndexOf(installed, name) < 0) continue;
                    font = Font.CreateDynamicFontFromOSFont(name, 32);
                    if (font != null) break;
                }
            }
            catch (System.Exception)
            {
                font = null;
            }

            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) _root.style.unityFontDefinition = FontDefinition.FromFont(font);
        }
    }
}
