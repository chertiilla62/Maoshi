using UnityEngine;

namespace Maoshi.Session
{
    /// <summary>
    /// Описание одной мини-игры: как называется, какая сцена, сколько рыбы за какое место.
    /// Новая мини-игра = новая сцена + новый такой ассет + строчка в каталоге. Остальной код не трогаем.
    /// </summary>
    [CreateAssetMenu(menuName = "Maoshi/Mini Game Definition", fileName = "MiniGame")]
    public class MiniGameDefinition : ScriptableObject
    {
        [Tooltip("Уникальный ID, латиницей. По нему сцена мини-игры сообщает результаты.")]
        [SerializeField] private string id = "new_mini_game";
        [SerializeField] private string displayName = "Новая мини-игра";
        [SerializeField, TextArea] private string description = "";
        [Tooltip("Имя сцены. Сцена должна быть в Build Settings.")]
        [SerializeField] private string sceneName = "";
        [SerializeField] private int minPlayers = 2;

        [Tooltip("Рыба за места: [0] — за 1-е место, [1] — за 2-е и т.д. Последнее значение — для всех ниже.")]
        [SerializeField] private int[] fishByPlace = { 10, 7, 5, 4, 3, 2, 1, 1 };

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public string SceneName => sceneName;
        public int MinPlayers => minPlayers;

        /// <summary>Сколько рыбы дать за место (1 — первое).</summary>
        public int FishForPlace(int place)
        {
            if (fishByPlace == null || fishByPlace.Length == 0) return 0;
            int index = Mathf.Clamp(place - 1, 0, fishByPlace.Length - 1);
            return Mathf.Max(0, fishByPlace[index]);
        }

#if UNITY_EDITOR
        /// <summary>Заполнение из скрипта настройки проекта.</summary>
        public void EditorSetup(string newId, string newName, string newDescription, string newScene, int[] newFish)
        {
            id = newId;
            displayName = newName;
            description = newDescription;
            sceneName = newScene;
            fishByPlace = newFish;
        }
#endif
    }
}
