using UnityEngine.UIElements;

namespace Maoshi.Session
{
    /// <summary>
    /// Мини-игра со своим окном по центру экрана (голосование, карточка роли и т.п.).
    /// HUD перестраивает окно, когда меняется ModalKey; пустой ключ — окна нет.
    /// </summary>
    public interface IMiniGameModal
    {
        string ModalKey { get; }
        VisualElement BuildModal();
    }
}
