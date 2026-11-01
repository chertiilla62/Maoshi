using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Окно консоли разработчика (верхняя часть экрана): журнал и строка ввода.
    /// ` (Ё) или F1 — открыть/закрыть, Enter — выполнить, ↑/↓ — история, Tab — дописать команду, Esc — закрыть.
    /// Пока открыто — кот не слушается, игровые клавиши молчат (GameKeyboard), курсор свободен.
    /// </summary>
    public class DevConsoleView
    {
        public VisualElement Root { get; }

        private readonly ScrollView _log;
        private readonly TextField _input;
        private bool _dirty = true;
        private int _historyIndex = -1;
        private int _focusFrames;

        public DevConsoleView()
        {
            Root = UIKit.Box("console");
            Root.pickingMode = PickingMode.Position;

            var header = UIKit.Row("console__header");
            header.Add(UIKit.Text("Консоль разработчика", "console__title"));
            header.Add(UIKit.Text("commands — все команды · Tab — дописать · ↑↓ — история · ` или Esc — закрыть", "console__hint"));
            Root.Add(header);

            _log = new ScrollView(ScrollViewMode.Vertical);
            _log.AddToClassList("console__log");
            Root.Add(_log);

            var inputRow = UIKit.Row("console__input-row");
            inputRow.Add(UIKit.Text(">", "console__prompt"));
            _input = new TextField();
            _input.AddToClassList("console__input");
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            // Символ клавиши открытия не должен попадать в строку.
            _input.RegisterValueChangedCallback(e =>
            {
                string clean = e.newValue.Replace("`", "").Replace("ё", "").Replace("Ё", "").Replace("~", "");
                if (clean != e.newValue) _input.SetValueWithoutNotify(clean);
            });
            inputRow.Add(_input);
            Root.Add(inputRow);

            DevConsole.LinesChanged += () => _dirty = true;
            UIKit.Show(Root, false);
        }

        /// <summary>Каждый кадр из MaoshiUI.</summary>
        public void Tick()
        {
            if (!DevConsole.Enabled) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.backquoteKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame))
                SetOpen(!DevConsole.IsOpen);
            else if (DevConsole.IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetOpen(false);

            if (!DevConsole.IsOpen) return;

            if (_dirty) Rebuild();
            // Фокус в строку ввода ставим пару кадров подряд: в кадр открытия UI Toolkit его ещё не принимает.
            if (_focusFrames > 0)
            {
                _focusFrames--;
                _input.Focus();
            }
        }

        public void SetOpen(bool open)
        {
            DevConsole.IsOpen = open;
            UIKit.Show(Root, open);
            if (open)
            {
                _input.SetValueWithoutNotify("");
                _historyIndex = -1;
                _focusFrames = 3;
                _dirty = true;
            }
            else
            {
                _input.Blur();
            }
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    DevConsole.Execute(_input.value);
                    _input.SetValueWithoutNotify("");
                    _historyIndex = -1;
                    _focusFrames = 2;
                    Stop(e);
                    break;
                case KeyCode.UpArrow:
                    StepHistory(1);
                    Stop(e);
                    break;
                case KeyCode.DownArrow:
                    StepHistory(-1);
                    Stop(e);
                    break;
                case KeyCode.Tab:
                    SetInput(DevConsole.Complete(_input.value));
                    Stop(e);
                    break;
                case KeyCode.BackQuote:
                case KeyCode.Escape:
                    Stop(e);
                    break;
            }
        }

        private static void Stop(KeyDownEvent e)
        {
            e.StopImmediatePropagation();
            e.StopPropagation();
        }

        private void StepHistory(int direction)
        {
            int next = Mathf.Clamp(_historyIndex + direction, -1, DevConsole.HistoryCount - 1);
            if (next == _historyIndex) return;
            _historyIndex = next;
            SetInput(next < 0 ? "" : DevConsole.History(next));
        }

        private void SetInput(string text)
        {
            _input.SetValueWithoutNotify(text ?? "");
            int end = _input.value.Length;
            _input.schedule.Execute(() => _input.SelectRange(end, end));
        }

        private void Rebuild()
        {
            _dirty = false;
            _log.Clear();
            foreach (var line in DevConsole.Lines)
            {
                var label = UIKit.Text(line.Text, "console__line", "console__line--" + line.Kind.ToString().ToLowerInvariant());
                label.enableRichText = false;
                _log.Add(label);
            }
            // Прокрутить вниз, когда раскладка пересчитается.
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(10);
        }
    }
}
