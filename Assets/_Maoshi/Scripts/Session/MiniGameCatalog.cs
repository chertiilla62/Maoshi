using System.Collections.Generic;
using UnityEngine;

namespace Maoshi.Session
{
    /// <summary>Список всех мини-игр, доступных в сессии.</summary>
    [CreateAssetMenu(menuName = "Maoshi/Mini Game Catalog", fileName = "MiniGameCatalog")]
    public class MiniGameCatalog : ScriptableObject
    {
        [SerializeField] private List<MiniGameDefinition> miniGames = new();

        public IReadOnlyList<MiniGameDefinition> MiniGames => miniGames;

        public MiniGameDefinition Find(string id)
        {
            foreach (var definition in miniGames)
            {
                if (definition != null && definition.Id == id) return definition;
            }

            return null;
        }

#if UNITY_EDITOR
        public void EditorAdd(MiniGameDefinition definition)
        {
            if (definition != null && !miniGames.Contains(definition)) miniGames.Add(definition);
        }

        /// <summary>Заменить игру другой на том же месте (номер в каталоге и клавиша отладки не меняются).</summary>
        public void EditorReplace(MiniGameDefinition old, MiniGameDefinition replacement)
        {
            if (replacement == null) return;
            int index = miniGames.IndexOf(old);
            if (index < 0) { EditorAdd(replacement); return; }
            miniGames.Remove(replacement);
            index = miniGames.IndexOf(old);
            miniGames[index] = replacement;
        }
#endif
    }
}
