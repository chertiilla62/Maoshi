using Maoshi.Cat;
using UnityEngine;

namespace Maoshi.Gameplay
{
    /// <summary>
    /// Предмет, который по-своему реагирует на толчок лапой (например, клубок улетает сильно).
    /// Вызывается только на сервере, после проверки честности толчка в CatController.
    /// Без этого интерфейса предмет получает обычный слабый импульс.
    /// </summary>
    public interface IPushReceiver
    {
        /// <param name="pusher">Кто толкнул.</param>
        /// <param name="direction">Горизонтальное направление от кота к предмету (нормализовано).</param>
        void OnPushedByCat(CatController pusher, Vector3 direction);
    }
}
