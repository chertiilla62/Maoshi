using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Maoshi.Session
{
    /// <summary>Что считаем за партию для номинаций.</summary>
    public enum Stat
    {
        Hits,          // ударил лапой другого кота
        Stunned,       // был оглушён
        Eliminated,    // вылетел из мини-игры
        Wins,          // 1-е место в мини-игре
        LastPlaces,    // последнее место
        FishEarned,    // рыба за места в мини-играх
        RouletteBet,   // поставил в рулетке
        RouletteWon,   // выиграл в рулетке
        Buyouts,       // перекупил зону
        Slippers,      // кинул тапок как зритель
        AuctionSpent,  // потратил на аукционах
        Count
    }

    /// <summary>Номинация в конце партии: титул, за что и кому.</summary>
    public struct Nomination : INetworkSerializable, IEquatable<Nomination>
    {
        public FixedString128Bytes Title;
        public FixedString128Bytes Detail;
        public ulong ClientId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref Detail);
            serializer.SerializeValue(ref ClientId);
        }

        public bool Equals(Nomination other) => Title.Equals(other.Title) && Detail.Equals(other.Detail) && ClientId == other.ClientId;
    }

    /// <summary>
    /// Статистика партии — только на сервере. Её пишут CatController, режимы мини-игр, SessionManager, рулетка, зрители.
    /// В финале из неё собираются номинации («Главный задира», «Звёздочки над головой» и т.д.).
    /// </summary>
    public static class SessionStats
    {
        private static readonly Dictionary<ulong, int[]> Values = new();

        public static void Reset() => Values.Clear();

        /// <summary>Добавить к статистике игрока (вызывать только на сервере).</summary>
        public static void Add(ulong clientId, Stat stat, int amount = 1)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || amount == 0) return;
            if (!Values.TryGetValue(clientId, out var row)) Values[clientId] = row = new int[(int)Stat.Count];
            row[(int)stat] += amount;
        }

        public static int Get(ulong clientId, Stat stat) => Values.TryGetValue(clientId, out var row) ? row[(int)stat] : 0;

        private struct Rule
        {
            public Stat Stat;
            public string Title;
            public Func<int, string> Detail;
        }

        // Порядок — от самых смешных к спокойным; показываем только те, где у кого-то не ноль.
        private static readonly Rule[] Rules =
        {
            new() { Stat = Stat.Stunned, Title = "Звёздочки над головой", Detail = n => $"оглушён {n} {Plural(n, "раз", "раза", "раз")}" },
            new() { Stat = Stat.Hits, Title = "Главный задира", Detail = n => $"раздал {n} {Plural(n, "удар", "удара", "ударов")}" },
            new() { Stat = Stat.Eliminated, Title = "Вечно вылетающий", Detail = n => $"вылетал {n} {Plural(n, "раз", "раза", "раз")}" },
            new() { Stat = Stat.Wins, Title = "Чемпион мини-игр", Detail = n => $"{n} {Plural(n, "победа", "победы", "побед")}" },
            new() { Stat = Stat.FishEarned, Title = "Рыбный магнат", Detail = n => $"наловил {n} {Plural(n, "рыбу", "рыбы", "рыб")}" },
            new() { Stat = Stat.RouletteBet, Title = "Азартный кот", Detail = n => $"поставил в рулетку {n} {Plural(n, "рыбу", "рыбы", "рыб")}" },
            new() { Stat = Stat.RouletteWon, Title = "Везунчик", Detail = n => $"выиграл в рулетку {n} {Plural(n, "рыбу", "рыбы", "рыб")}" },
            new() { Stat = Stat.Buyouts, Title = "Перекупщик", Detail = n => $"перекупил {n} {Plural(n, "зону", "зоны", "зон")}" },
            new() { Stat = Stat.Slippers, Title = "Тапкометатель", Detail = n => $"кинул {n} {Plural(n, "тапок", "тапка", "тапок")}" },
            new() { Stat = Stat.AuctionSpent, Title = "Транжира", Detail = n => $"отдал на аукционах {n} {Plural(n, "рыбу", "рыбы", "рыб")}" },
            new() { Stat = Stat.LastPlaces, Title = "Утешительный приз", Detail = n => $"последний {n} {Plural(n, "раз", "раза", "раз")}" },
        };

        /// <summary>Собрать номинации: у каждой — игрок с наибольшим значением (ничья — кто раньше в списке игроков).</summary>
        public static List<Nomination> Build(IReadOnlyList<ulong> players, int max = 8)
        {
            var result = new List<Nomination>();
            foreach (var rule in Rules)
            {
                ulong best = ulong.MaxValue;
                int bestValue = 0;
                foreach (var id in players)
                {
                    int value = Get(id, rule.Stat);
                    if (value > bestValue) { bestValue = value; best = id; }
                }
                if (best == ulong.MaxValue) continue;
                result.Add(new Nomination
                {
                    Title = new FixedString128Bytes(rule.Title),
                    Detail = new FixedString128Bytes(rule.Detail(bestValue)),
                    ClientId = best
                });
                if (result.Count >= max) break;
            }
            return result;
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }
    }
}
