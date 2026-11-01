using System;
using Unity.Netcode;

namespace Maoshi.Session
{
    /// <summary>Зоны двора — то, за что борются коты. В финале побеждает тот, у кого их больше.</summary>
    public static class YardZones
    {
        public static readonly string[] Names =
        {
            "Крыша",
            "Помойка",
            "Подвал",
            "Подъезд",
            "Гаражи",
            "Песочница",
            "Лавочка у подъезда",
            "Голубятня",
            "Чердак",
            "Детская площадка"
        };

        public const int MinZones = 4;

        /// <summary>Сколько зон в партии: на две больше, чем игроков, чтобы в начале всем хватало.</summary>
        public static int CountFor(int players) => Math.Clamp(players + 2, MinZones, Names.Length);

        public static string NameOf(int zoneId) =>
            zoneId >= 0 && zoneId < Names.Length ? Names[zoneId] : $"Зона {zoneId}";
    }

    /// <summary>Состояние зоны: кто владеет, сколько за неё заплатили и в каком раунде её нельзя перекупить.</summary>
    public struct ZoneState : INetworkSerializable, IEquatable<ZoneState>
    {
        public byte ZoneId;
        public bool HasOwner;
        public ulong Owner;
        /// <summary>Сколько рыбы последний хозяин заплатил за зону (аукцион или перекуп).</summary>
        public int Price;
        /// <summary>Зону купили в этом раунде — перекупить можно только со следующего.</summary>
        public int LockedRound;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ZoneId);
            serializer.SerializeValue(ref HasOwner);
            serializer.SerializeValue(ref Owner);
            serializer.SerializeValue(ref Price);
            serializer.SerializeValue(ref LockedRound);
        }

        public bool Equals(ZoneState other) => ZoneId == other.ZoneId && HasOwner == other.HasOwner && Owner == other.Owner
                                               && Price == other.Price && LockedRound == other.LockedRound;
    }

    /// <summary>Этап аукциона.</summary>
    public enum AuctionStage : byte
    {
        None,
        /// <summary>Игроки тайно ставят рыбу.</summary>
        Bidding,
        /// <summary>Ничья — идёт «Кошачья разборка» между лидерами.</summary>
        Showdown,
        /// <summary>Ставки открыты, объявлен победитель.</summary>
        Reveal
    }
}
