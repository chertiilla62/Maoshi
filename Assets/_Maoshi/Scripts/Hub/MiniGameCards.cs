using System.Collections.Generic;
using UnityEngine;

namespace Maoshi.Hub
{
    /// <summary>
    /// Короткие карточки мини-игр для голосования: жанр (с цветом) и одна строчка «о чём игра».
    /// Полное описание лежит в MiniGameDefinition — оно длинное, на карточку не помещается.
    /// Новая игра без карточки получит жанр «Мини-игра» и начало описания.
    /// </summary>
    public static class MiniGameCards
    {
        /// <summary>Особый вариант голосования: «Кот в мешке» — случайная игра.</summary>
        public const string RandomId = "random";

        public readonly struct Card
        {
            public readonly string Genre;
            public readonly Color GenreColor;
            public readonly string Tagline;

            public Card(string genre, Color color, string tagline)
            {
                Genre = genre;
                GenreColor = color;
                Tagline = tagline;
            }
        }

        private static readonly Color Brawl = new(0.86f, 0.30f, 0.24f);
        private static readonly Color Agility = new(0.20f, 0.62f, 0.86f);
        private static readonly Color Stealth = new(0.42f, 0.32f, 0.70f);
        private static readonly Color Catch = new(0.22f, 0.66f, 0.38f);
        private static readonly Color Rhythm = new(0.90f, 0.42f, 0.70f);
        private static readonly Color Mind = new(0.90f, 0.60f, 0.12f);

        private static readonly Dictionary<string, Card> Cards = new()
        {
            ["last_on_fence"] = new Card("Толкотня", Brawl, "Удержись на заборе последним"),
            ["fly_hunt"] = new Card("Ловкость", Agility, "Допрыгни и поймай муху"),
            ["sun_spot"] = new Card("Толкотня", Brawl, "Грейся в пятне солнца, толкай других"),
            ["hot_pie"] = new Card("Выживание", Brawl, "Передай горячий пирожок толчком"),
            ["sour_cream"] = new Card("Детектив", Mind, "Лизни сметану и не попадись"),
            ["yarn_ram"] = new Card("Ловкость", Agility, "Закати клубки в свою корзинку"),
            ["janitor_escape"] = new Card("Выживание", Agility, "Не попадись дворнику"),
            ["pigeon_race"] = new Card("Гонка", Agility, "Беги по крышам за голубем"),
            ["who_broke_vase"] = new Card("Детектив", Mind, "Найди, кто роняет вазы"),
            ["sausage_kitchen"] = new Card("Хитрость", Stealth, "Стащи колбасу, пока не видят"),
            ["night_concert"] = new Card("Ритм", Rhythm, "Пой в такт под луной"),
            ["window_heist"] = new Card("Хитрость", Stealth, "Обнеси квартиру, не разбуди пса"),
            ["pond_fishing"] = new Card("Ловля", Catch, "Бей лапой по рыбе в пруду"),
            ["mouse_holes"] = new Card("Ловля", Catch, "Хватай мышей из норок"),
            ["last_in_box"] = new Card("Выживание", Brawl, "Влезь в коробку, пока она есть"),
            [RandomId] = new Card("Сюрприз", new Color(0.55f, 0.55f, 0.60f), "Любая игра — узнаешь в конце"),
        };

        /// <summary>Карточка игры. Если её нет — жанр «Мини-игра» и начало полного описания.</summary>
        public static Card Get(string id, string fallbackDescription)
        {
            if (Cards.TryGetValue(id, out var card)) return card;
            string tagline = fallbackDescription ?? "";
            int cut = tagline.IndexOfAny(new[] { '.', '!', '?' });
            if (cut > 0) tagline = tagline.Substring(0, cut);
            if (tagline.Length > 48) tagline = tagline.Substring(0, 46) + "…";
            return new Card("Мини-игра", new Color(0.5f, 0.5f, 0.55f), tagline);
        }

        /// <summary>Цвет круга голосования по номеру: у каждого варианта свой, «мешок» — фиолетовый.</summary>
        public static Color PadColor(int index, bool isRandom)
        {
            if (isRandom) return new Color(0.62f, 0.45f, 0.95f);
            switch (index % 4)
            {
                case 0: return new Color(0.98f, 0.55f, 0.35f);
                case 1: return new Color(0.35f, 0.80f, 0.95f);
                case 2: return new Color(0.55f, 0.90f, 0.40f);
                default: return new Color(0.98f, 0.82f, 0.30f);
            }
        }
    }
}
