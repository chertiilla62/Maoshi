using UnityEngine.InputSystem;

namespace Maoshi
{
    /// <summary>
    /// Клавиатура для игровых клавиш (E, Q, цифры, F-клавиши…). Пока открыта консоль разработчика — null:
    /// набранный в консоли текст не мяукает, не лижет сметану и не запускает мини-игры.
    /// Весь игровой код читает клавиатуру через это свойство, а не через Keyboard.current.
    /// </summary>
    public static class GameKeyboard
    {
        public static Keyboard Current => DevConsole.IsOpen ? null : Keyboard.current;
    }
}
