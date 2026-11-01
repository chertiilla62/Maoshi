using System.Collections.Generic;
using Maoshi.Showdown;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Самопроверка правил «Кошачьей свалки» без запуска игры: меню Maoshi → Test → Check Brawl Rules.
    /// Каждый сценарий — маленький бой с заранее выбранными картами; результат сравнивается с ожидаемым.
    /// </summary>
    public static class MaoshiBrawlRulesCheck
    {
        private static int _failed;

        [MenuItem("Maoshi/Test/Check Brawl Rules")]
        public static void Run()
        {
            _failed = 0;

            Scenario("Царапка: 1 урон", f =>
            {
                Play(f[0], ShowdownCard.Scratch, f[1]);
                Resolve(f);
                Expect(f[1].Lives == 4, "у B 4 жизни");
            });

            Scenario("Укус раскрывает", f =>
            {
                Play(f[0], ShowdownCard.Bite, f[1]);
                Play(f[2], ShowdownCard.Scratch, f[0]);
                Resolve(f);
                Expect(f[1].Lives == 3, "B −2");
                Expect(f[0].Lives == 3, "A (кусал) получил 1+1");
            });

            Scenario("Прыжок с забора: промах, если цель бьёт тебя", f =>
            {
                Play(f[0], ShowdownCard.FenceJump, f[1]);
                Play(f[1], ShowdownCard.Scratch, f[0]);
                Resolve(f);
                Expect(f[1].Lives == 5, "B цел");
                Expect(f[0].Lives == 4, "A −1");
            });

            Scenario("Прыжок с забора: попал", f =>
            {
                Play(f[0], ShowdownCard.FenceJump, f[1]);
                Play(f[1], ShowdownCard.Scratch, f[2]);
                Resolve(f);
                Expect(f[1].Lives == 2, "B −3");
            });

            Scenario("Шипение: блок 2 и ответка первому", f =>
            {
                Play(f[0], ShowdownCard.Hiss, null);
                Play(f[1], ShowdownCard.Scratch, f[0]);
                Play(f[2], ShowdownCard.Bite, f[0]);
                Resolve(f);
                Expect(f[0].Lives == 4, "A получил 1 из 3");
                Expect(f[1].Lives == 4, "B получил ответку");
                Expect(f[2].Lives == 5, "C без ответки");
            });

            Scenario("Коробка: неуязвимость и запрет атак", f =>
            {
                Play(f[0], ShowdownCard.Box, null);
                Play(f[1], ShowdownCard.Bite, f[0]);
                Resolve(f);
                Expect(f[0].Lives == 5, "A цел");
                Expect(f[0].BoxLocked, "A не может атаковать в следующий ход");
                Expect(!BrawlRules.IsValidPick(f[0], new BrawlPick { Card = ShowdownCard.Scratch, Target = f[1].Id }, f), "атака запрещена");
            });

            Scenario("Подстава", f =>
            {
                Play(f[0], ShowdownCard.Frame, f[2]);
                Play(f[1], ShowdownCard.Scratch, f[0]);
                Resolve(f);
                Expect(f[0].Lives == 5, "A цел");
                Expect(f[2].Lives == 4, "удар достался C");
            });

            Scenario("Мусорка", f =>
            {
                Play(f[0], ShowdownCard.TrashCan, null);
                Resolve(f);
                Expect(f[0].Lives == 5 && f[1].Lives == 4 && f[2].Lives == 4, "все, кроме A, −1");
            });

            Scenario("Мурлыканье: союз и мир", f =>
            {
                Play(f[0], ShowdownCard.Purr, f[1]);
                Play(f[1], ShowdownCard.Purr, f[0]);
                Resolve(f);
                Expect(f[0].Lives == 6 && f[1].Lives == 6, "оба +1");
                Expect(f[0].IsAlliedWith(f[1].Id), "союз заключён");

                Play(f[0], ShowdownCard.Scratch, f[1]);
                Play(f[1], ShowdownCard.None, null);
                Play(f[2], ShowdownCard.None, null);
                Resolve(f);
                Expect(f[1].Lives == 6, "союзника не бьют (ход 1)");

                Play(f[0], ShowdownCard.Scratch, f[1]);
                Resolve(f);
                Expect(f[1].Lives == 6, "союзника не бьют (ход 2)");

                Play(f[0], ShowdownCard.Scratch, f[1]);
                Resolve(f);
                Expect(f[1].Lives == 5, "союз закончился");
            });

            Scenario("Мурлыканье без ответа", f =>
            {
                Play(f[0], ShowdownCard.Purr, f[1]);
                Resolve(f);
                Expect(f[0].Lives == 5 && !f[0].IsAlliedWith(f[1].Id), "ничего не случилось");
            });

            Scenario("Блохи и Валерьянка", f =>
            {
                Play(f[0], ShowdownCard.Fleas, f[1]);
                Resolve(f);
                Expect(f[1].Fleas == 2, "блохи на 2 хода");
                BrawlRules.TickFleas(f, new List<string>());
                Expect(f[1].Lives == 4, "укус 1");
                Play(f[1], ShowdownCard.Valerian, null);
                Resolve(f);
                Expect(f[1].Lives == 5 && f[1].Fleas == 0, "вылечился и без блох");
            });

            Scenario("Выбывание и победитель", f =>
            {
                f[1].Lives = 1;
                f[2].Lives = 1;
                Play(f[0], ShowdownCard.TrashCan, null);
                Resolve(f);
                Expect(!f[1].Alive && !f[2].Alive, "B и C выбыли");
                Expect(BrawlRules.PickWinner(f, new List<BrawlFighter>(), 0.5) == f[0].Id, "победил A");
            });

            Scenario("Все выбыли разом — выше ставка", f =>
            {
                foreach (var x in f) x.Lives = 1;
                f[1].Bid = 7;
                Play(f[0], ShowdownCard.TrashCan, null);
                Play(f[1], ShowdownCard.TrashCan, null);
                Play(f[2], ShowdownCard.TrashCan, null);
                var before = new List<BrawlFighter>(f);
                Resolve(f);
                Expect(BrawlRules.PickWinner(f, before, 0.1) == f[1].Id, "победил B со ставкой 7");
            });

            Scenario("Колода — 20 карт", f =>
            {
                Expect(BrawlRules.BuildDeck().Count == 20, "20 карт");
            });

            if (_failed == 0) Debug.Log("[Maoshi Rules] Все проверки свалки пройдены ✔");
            else Debug.LogError($"[Maoshi Rules] Провалено проверок: {_failed}");
        }

        // ───────────────────────── Помощники ─────────────────────────

        private static string _current;

        private static void Scenario(string name, System.Action<List<BrawlFighter>> body)
        {
            _current = name;
            var fighters = new List<BrawlFighter>
            {
                new() { Id = 1, Name = "A", Lives = BrawlRules.StartLives },
                new() { Id = 2, Name = "B", Lives = BrawlRules.StartLives },
                new() { Id = 3, Name = "C", Lives = BrawlRules.StartLives },
            };
            body(fighters);
        }

        private static void Play(BrawlFighter fighter, ShowdownCard card, BrawlFighter target)
        {
            fighter.Pick = new BrawlPick { Card = card, Target = target != null ? target.Id : BrawlRules.NoOne };
            fighter.HasPick = card != ShowdownCard.None;
        }

        private static void Resolve(List<BrawlFighter> fighters)
        {
            var log = new List<string>();
            BrawlRules.ResolveTurn(fighters, log);
            // Следующий ход начинается «с чистого листа»: без выбранных карт.
            foreach (var f in fighters) f.HasPick = false;
        }

        private static void Expect(bool condition, string what)
        {
            if (condition) return;
            _failed++;
            Debug.LogError($"[Maoshi Rules] «{_current}»: ожидалось — {what}");
        }
    }
}
