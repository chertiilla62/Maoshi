using System.Collections.Generic;
using System.Text;

namespace Maoshi.Showdown
{
    /// <summary>Карты «Кошачьей свалки». Номера не менять — они передаются по сети байтом.</summary>
    public enum ShowdownCard : byte
    {
        None = 0,
        Scratch = 1,    // Царапка
        Bite = 2,       // Укус
        FenceJump = 3,  // Прыжок с забора
        TrashCan = 4,   // Опрокинуть мусорку
        Hiss = 5,       // Шипение
        Box = 6,        // Коробка
        Frame = 7,      // Подстава
        Fleas = 8,      // Блохи
        Purr = 9,       // Мурлыканье
        Valerian = 10,  // Валерьянка
    }

    public enum CardType : byte { Attack, Defense, Trick, Care }

    /// <summary>Описание карты для интерфейса и правил.</summary>
    public readonly struct CardInfo
    {
        public readonly string Name;
        public readonly CardType Type;
        public readonly string Power;     // что в кружке
        public readonly bool NeedsTarget; // нужно выбрать кота
        public readonly string Main;      // главная строка описания (с <b>)
        public readonly string Sub;       // уточнение мелким шрифтом
        public readonly int Copies;       // сколько в колоде

        public CardInfo(string name, CardType type, string power, bool needsTarget, string main, string sub, int copies)
        {
            Name = name;
            Type = type;
            Power = power;
            NeedsTarget = needsTarget;
            Main = main;
            Sub = sub;
            Copies = copies;
        }
    }

    /// <summary>Что выбрал боец в этот ход.</summary>
    public struct BrawlPick
    {
        public ShowdownCard Card;
        public ulong Target;
    }

    /// <summary>Состояние бойца для расчёта хода (только сервер).</summary>
    public class BrawlFighter
    {
        public ulong Id;
        public string Name;
        public int Lives;
        public bool Alive = true;
        public int Fleas;          // сколько ходов ещё кусают блохи
        public bool BoxLocked;     // в этот ход нельзя атаковать (после Коробки)
        public ulong AllyId = BrawlRules.NoOne;
        public int AllyTurns;      // сколько ходов ещё действует союз
        public int Bid;            // ставка на аукционе (для ничьей)
        public BrawlPick Pick;
        public bool HasPick;
        public readonly List<ShowdownCard> Hand = new();
        public readonly List<ShowdownCard> Deck = new();

        public bool IsAlliedWith(ulong other) => AllyTurns > 0 && AllyId == other;
    }

    /// <summary>
    /// Правила «Кошачьей свалки» — чистая логика без сети, чтобы её было легко проверять и менять.
    /// Ход разрешается строго по фазам:
    /// 1) Коробка (неуязвимость) и Подстава (перенаправление); 2) защита (Шипение);
    /// 3) все атаки одновременно; 4) Валерьянка, Блохи, Мурлыканье; 5) выбывание.
    /// Блохи кусают в начале хода (TickFleas).
    /// </summary>
    public static class BrawlRules
    {
        public const ulong NoOne = ulong.MaxValue;
        public const int StartLives = 5;
        public const int MaxLives = 6;
        public const int HandSize = 5;
        public const int MaxHandSize = 7;
        public const int MaxTurns = 8;
        public const int HissBlock = 2;
        public const int AllianceTurns = 2;
        public const int FleasTurns = 2;
        /// <summary>В свалку попадают все, чья ставка отстаёт от лучшей не больше чем на столько рыб.</summary>
        public const int BidWindow = 2;

        private static readonly Dictionary<ShowdownCard, CardInfo> Infos = new()
        {
            [ShowdownCard.Scratch] = new CardInfo("Царапка", CardType.Attack, "1", true, "<b>1 урон</b> коту", "", 5),
            [ShowdownCard.Bite] = new CardInfo("Укус", CardType.Attack, "2", true, "<b>2 урона</b> коту", "но удары по тебе в этот ход <b>+1</b>", 2),
            [ShowdownCard.FenceJump] = new CardInfo("Прыжок с забора", CardType.Attack, "3", true, "<b>3 урона</b> коту", "промах, если он в этот ход бьёт тебя", 2),
            [ShowdownCard.TrashCan] = new CardInfo("Опрокинуть мусорку", CardType.Attack, "1", false, "<b>1 урон</b> каждому", "кроме тебя", 1),
            [ShowdownCard.Hiss] = new CardInfo("Шипение", CardType.Defense, "2", false, "Блок <b>2 урона</b>", "первый напавший получает <b>1</b>", 3),
            [ShowdownCard.Box] = new CardInfo("Коробка", CardType.Defense, "∞", false, "Тебя <b>не достать</b> весь ход", "следующий ход — без атак", 1),
            [ShowdownCard.Frame] = new CardInfo("Подстава", CardType.Trick, "↷", true, "<b>Все удары</b> по тебе", "достаются выбранному коту", 2),
            [ShowdownCard.Fleas] = new CardInfo("Блохи", CardType.Trick, "2", true, "Цель теряет <b>1 жизнь</b>", "в начале двух следующих ходов", 1),
            [ShowdownCard.Purr] = new CardInfo("Мурлыканье", CardType.Trick, "+1", true, "Мурлычете друг другу — <b>союз</b>", "<b>+1 жизнь</b> обоим и 2 хода мира", 1),
            [ShowdownCard.Valerian] = new CardInfo("Валерьянка", CardType.Care, "+1", false, "<b>+1 жизнь</b> (до 6)", "и прогоняет блох", 2),
        };

        public static CardInfo Info(ShowdownCard card) =>
            Infos.TryGetValue(card, out var info) ? info : new CardInfo("Пас", CardType.Care, "—", false, "Пропуск хода", "", 0);

        public static string NameOf(ShowdownCard card) => Info(card).Name;

        public static bool IsAttack(ShowdownCard card) => card != ShowdownCard.None && Info(card).Type == CardType.Attack;

        /// <summary>Колода одного кота: 20 карт.</summary>
        public static List<ShowdownCard> BuildDeck()
        {
            var deck = new List<ShowdownCard>();
            foreach (var pair in Infos)
                for (int i = 0; i < pair.Value.Copies; i++) deck.Add(pair.Key);
            return deck;
        }

        /// <summary>Можно ли бойцу сыграть эту карту на эту цель.</summary>
        public static bool IsValidPick(BrawlFighter fighter, BrawlPick pick, IReadOnlyList<BrawlFighter> all)
        {
            if (pick.Card == ShowdownCard.None) return true;
            if (fighter.BoxLocked && IsAttack(pick.Card)) return false;
            if (!Info(pick.Card).NeedsTarget) return true;

            if (pick.Target == fighter.Id) return false;
            foreach (var other in all)
                if (other.Id == pick.Target) return other.Alive;
            return false;
        }

        // ───────────────────────── Начало хода: блохи ─────────────────────────

        /// <summary>Блохи кусают тех, у кого они есть.</summary>
        public static void TickFleas(IReadOnlyList<BrawlFighter> fighters, List<string> log)
        {
            foreach (var fighter in fighters)
            {
                if (!fighter.Alive || fighter.Fleas <= 0) continue;
                fighter.Fleas--;
                fighter.Lives = System.Math.Max(0, fighter.Lives - 1);
                log.Add($"Блохи кусают: {fighter.Name} −1");
                if (fighter.Lives <= 0)
                {
                    fighter.Alive = false;
                    log.Add($"{fighter.Name} убегает чесаться!");
                }
            }
        }

        // ───────────────────────── Разрешение хода ─────────────────────────

        /// <summary>
        /// Разрешить ход: у живых бойцов в Pick лежит выбранная карта (HasPick = false — пропуск).
        /// Меняет жизни, блох, союзы и запреты; пишет понятный журнал.
        /// </summary>
        public static void ResolveTurn(IReadOnlyList<BrawlFighter> fighters, List<string> log)
        {
            var alive = new List<BrawlFighter>();
            foreach (var f in fighters) if (f.Alive) alive.Add(f);

            BrawlFighter Find(ulong id)
            {
                foreach (var f in alive) if (f.Id == id) return f;
                return null;
            }

            ShowdownCard CardOf(BrawlFighter f) => f.HasPick ? f.Pick.Card : ShowdownCard.None;

            var damage = new Dictionary<ulong, int>();
            foreach (var f in alive) damage[f.Id] = 0;

            // Запрет атак после Коробки действует ровно один следующий ход.
            var lockedNext = new HashSet<ulong>();

            // 1. Коробка и Подстава.
            var immune = new HashSet<ulong>();
            var redirect = new Dictionary<ulong, ulong>();
            foreach (var f in alive)
            {
                switch (CardOf(f))
                {
                    case ShowdownCard.Box:
                        immune.Add(f.Id);
                        lockedNext.Add(f.Id);
                        log.Add($"{f.Name} прячется в коробку");
                        break;
                    case ShowdownCard.Frame:
                        if (Find(f.Pick.Target) != null) redirect[f.Id] = f.Pick.Target;
                        break;
                }
            }

            // 2. Защита.
            var block = new Dictionary<ulong, int>();
            var counterReady = new HashSet<ulong>();
            foreach (var f in alive)
            {
                if (CardOf(f) != ShowdownCard.Hiss) continue;
                block[f.Id] = HissBlock;
                counterReady.Add(f.Id);
            }

            // Атакует ли кот в этот ход данного соперника (для Прыжка с забора).
            bool Attacks(BrawlFighter attacker, ulong victim)
            {
                var card = CardOf(attacker);
                if (card == ShowdownCard.TrashCan) return attacker.Id != victim;
                return IsAttack(card) && attacker.Pick.Target == victim;
            }

            // 3. Атаки — все одновременно (урон копится и вычитается в конце).
            var hits = new List<(BrawlFighter attacker, ulong victim, int amount, ShowdownCard card)>();
            foreach (var f in alive)
            {
                var card = CardOf(f);
                switch (card)
                {
                    case ShowdownCard.Scratch:
                        hits.Add((f, f.Pick.Target, 1, card));
                        break;
                    case ShowdownCard.Bite:
                        hits.Add((f, f.Pick.Target, 2, card));
                        break;
                    case ShowdownCard.FenceJump:
                        var target = Find(f.Pick.Target);
                        if (target != null && Attacks(target, f.Id))
                            log.Add($"{f.Name} прыгает на {target.Name} — промах!");
                        else
                            hits.Add((f, f.Pick.Target, 3, card));
                        break;
                    case ShowdownCard.TrashCan:
                        log.Add($"{f.Name} опрокидывает мусорку на всех!");
                        foreach (var other in alive)
                            if (other.Id != f.Id) hits.Add((f, other.Id, 1, card));
                        break;
                }
            }

            foreach (var (attacker, originalVictim, baseAmount, card) in hits)
            {
                ulong victimId = originalVictim;
                string via = "";

                // Подстава: удар уходит другому (один раз, без цепочек).
                if (redirect.TryGetValue(victimId, out ulong framed))
                {
                    var from = Find(victimId);
                    via = from != null ? $" (подстава {from.Name})" : "";
                    victimId = framed;
                }

                var victim = Find(victimId);
                if (victim == null) continue;
                string what = card == ShowdownCard.TrashCan ? "мусор" : NameOf(card);

                if (attacker.IsAlliedWith(victim.Id))
                {
                    log.Add($"{attacker.Name} не трогает союзника {victim.Name}");
                    continue;
                }

                if (immune.Contains(victim.Id))
                {
                    log.Add($"{attacker.Name} {what} → {victim.Name}: в коробке!");
                    continue;
                }

                int amount = baseAmount;
                if (CardOf(victim) == ShowdownCard.Bite) amount += 1; // укусивший раскрылся

                if (block.TryGetValue(victim.Id, out int left) && left > 0)
                {
                    int blocked = System.Math.Min(left, amount);
                    block[victim.Id] = left - blocked;
                    amount -= blocked;

                    // Первый напавший на шипящего получает ответку.
                    if (counterReady.Remove(victim.Id) && attacker.Id != victim.Id)
                    {
                        damage[attacker.Id] += 1;
                        log.Add($"{victim.Name} шипит на {attacker.Name}: −1 в ответ");
                    }
                }

                if (amount <= 0)
                {
                    log.Add($"{attacker.Name} {what} → {victim.Name}{via}: блок");
                    continue;
                }

                damage[victim.Id] += amount;
                log.Add($"{attacker.Name} {what} → {victim.Name}{via}: −{amount}");
            }

            foreach (var f in alive)
                f.Lives = System.Math.Max(0, f.Lives - damage[f.Id]);

            // 4. Забота, блохи и союзы.
            foreach (var f in alive)
            {
                var card = CardOf(f);
                if (card == ShowdownCard.Valerian && f.Lives > 0)
                {
                    f.Lives = System.Math.Min(MaxLives, f.Lives + 1);
                    f.Fleas = 0;
                    log.Add($"{f.Name} пьёт валерьянку: +1");
                }
                else if (card == ShowdownCard.Fleas)
                {
                    var target = Find(f.Pick.Target);
                    if (target != null && !immune.Contains(target.Id))
                    {
                        target.Fleas = FleasTurns;
                        log.Add($"{f.Name} подселяет блох к {target.Name}");
                    }
                }
                else if (card == ShowdownCard.Purr)
                {
                    var partner = Find(f.Pick.Target);
                    bool mutual = partner != null && CardOf(partner) == ShowdownCard.Purr && partner.Pick.Target == f.Id;
                    if (mutual && f.Id < partner.Id && f.Lives > 0 && partner.Lives > 0)
                    {
                        f.Lives = System.Math.Min(MaxLives, f.Lives + 1);
                        partner.Lives = System.Math.Min(MaxLives, partner.Lives + 1);
                        f.AllyId = partner.Id;
                        partner.AllyId = f.Id;
                        // +1: в конце этого хода счётчик уменьшится, а мир должен длиться 2 следующих хода.
                        f.AllyTurns = AllianceTurns + 1;
                        partner.AllyTurns = AllianceTurns + 1;
                        log.Add($"{f.Name} и {partner.Name} мурлычут — союз!");
                    }
                    else if (!mutual && partner != null)
                    {
                        log.Add($"{f.Name} мурлычет {partner.Name}, но без ответа");
                    }
                }
            }

            // 5. Выбывание и конец хода.
            foreach (var f in alive)
            {
                if (f.Lives <= 0)
                {
                    f.Alive = false;
                    log.Add($"{f.Name} выбывает!");
                }

                f.BoxLocked = lockedNext.Contains(f.Id);
                if (f.AllyTurns > 0)
                {
                    f.AllyTurns--;
                    if (f.AllyTurns == 0) f.AllyId = NoOne;
                }
            }

            if (log.Count == 0) log.Add("Все осторожничают — никто не напал");
        }

        /// <summary>
        /// Кто победил: последний живой; иначе (лимит ходов или выбыли все сразу) — больше жизней,
        /// потом выше ставка. Если и тут ничья — случайно (random — число 0..1).
        /// </summary>
        public static ulong PickWinner(IReadOnlyList<BrawlFighter> fighters, IReadOnlyList<BrawlFighter> diedLastTurn, double random)
        {
            var candidates = new List<BrawlFighter>();
            foreach (var f in fighters) if (f.Alive) candidates.Add(f);
            if (candidates.Count == 0) candidates.AddRange(diedLastTurn); // выбыли все одновременно
            if (candidates.Count == 0) return NoOne;

            candidates.Sort((a, b) =>
            {
                int lives = b.Lives.CompareTo(a.Lives);
                return lives != 0 ? lives : b.Bid.CompareTo(a.Bid);
            });

            var best = candidates[0];
            var tied = new List<BrawlFighter>();
            foreach (var f in candidates)
                if (f.Lives == best.Lives && f.Bid == best.Bid) tied.Add(f);

            int index = System.Math.Min(tied.Count - 1, (int)(random * tied.Count));
            return tied[index].Id;
        }

        /// <summary>Обрезать строку, чтобы она влезла в FixedString128Bytes (кириллица — 2 байта на букву).</summary>
        public static string FitBytes(string text, int maxBytes = 120)
        {
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
            var builder = new StringBuilder();
            int bytes = 0;
            foreach (char c in text)
            {
                int size = Encoding.UTF8.GetByteCount(c.ToString());
                if (bytes + size > maxBytes - 3) break;
                builder.Append(c);
                bytes += size;
            }

            return builder.Append('…').ToString();
        }
    }
}
