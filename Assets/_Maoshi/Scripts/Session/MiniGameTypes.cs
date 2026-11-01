using System.Collections.Generic;
using Unity.Netcode;

namespace Maoshi.Session
{
    /// <summary>Фаза мини-игры.</summary>
    public enum MiniGamePhase : byte
    {
        /// <summary>Сцена загружается у игроков.</summary>
        WaitingForPlayers,
        /// <summary>Отсчёт «3, 2, 1» — коты стоят на местах.</summary>
        Countdown,
        /// <summary>Идёт игра.</summary>
        Playing,
        /// <summary>Игра окончена, скоро покажем итоги.</summary>
        Finished
    }

    /// <summary>Фаза всей сессии.</summary>
    public enum SessionPhase : byte
    {
        /// <summary>Двор: голосование за следующую мини-игру.</summary>
        Hub,
        /// <summary>Идёт мини-игра.</summary>
        MiniGame,
        /// <summary>Экран итогов мини-игры.</summary>
        Results,
        /// <summary>Двор: аукцион за зону.</summary>
        Auction,
        /// <summary>Партия окончена, объявлен Король двора.</summary>
        Final
    }

    /// <summary>
    /// Общий интерфейс мини-игры. Всё, что о мини-игре нужно знать остальным системам (UI, сессии),
    /// доступно через него — без знания конкретных правил.
    /// </summary>
    public interface IGameMode
    {
        string MiniGameId { get; }
        MiniGamePhase Phase { get; }

        /// <summary>Сколько секунд осталось до конца текущей фазы (отсчёта или игры).</summary>
        float TimeLeft { get; }

        /// <summary>Сколько игроков ещё в игре.</summary>
        int AliveCount { get; }

        bool IsAlive(ulong clientId);

        /// <summary>Подсказка игроку под таймером («Лови мышей!», «Коробка сжимается через 3 с»).</summary>
        string StatusText { get; }
    }

    /// <summary>Место игрока по итогам мини-игры (1 — первое). Делёж мест разрешён.</summary>
    public struct Placement
    {
        public ulong ClientId;
        public int Place;

        public Placement(ulong clientId, int place)
        {
            ClientId = clientId;
            Place = place;
        }
    }

    /// <summary>Строка итогов для показа всем игрокам.</summary>
    public struct ResultEntry : INetworkSerializable
    {
        public ulong ClientId;
        public int Place;
        public int FishAwarded;
        public int TotalFish;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Place);
            serializer.SerializeValue(ref FishAwarded);
            serializer.SerializeValue(ref TotalFish);
        }
    }

    /// <summary>Сортировка итогов по месту.</summary>
    public static class ResultsUtil
    {
        public static void SortByPlace(List<ResultEntry> entries) => entries.Sort((a, b) => a.Place.CompareTo(b.Place));
    }
}
