using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Окраски котов: что видит игрок в меню. Порядок совпадает с массивом окрасов в CatAppearance (собирает Stage 19)
    /// и с текстурами Art/Textures/Cats/CatSkin_{Id}.png.
    /// Окраска не обязана быть уникальной: цвет игрока в интерфейсе (NetworkPlayer.ColorIndex) по-прежнему у каждого свой.
    /// </summary>
    public static class CatSkins
    {
        public struct Info
        {
            public string Id;
            public string Name;
            public Color Swatch;
        }

        public static readonly Info[] All =
        {
            new() { Id = "Ginger", Name = "Рыжий полосатый", Swatch = Hex(0xee8a2e) },
            new() { Id = "Black", Name = "Чёрный", Swatch = Hex(0x2b2832) },
            new() { Id = "White", Name = "Белый", Swatch = Hex(0xf6f3ec) },
            new() { Id = "GrayTabby", Name = "Серый полосатый", Swatch = Hex(0x8f949e) },
            new() { Id = "Tuxedo", Name = "Смокинг", Swatch = Hex(0x26232c) },
            new() { Id = "Calico", Name = "Трёхцветная", Swatch = Hex(0xe98a35) },
            new() { Id = "Siamese", Name = "Сиамская", Swatch = Hex(0xf1e2c6) },
            new() { Id = "BlueBritish", Name = "Голубой британец", Swatch = Hex(0x7f8ea8) },
            new() { Id = "Cream", Name = "Кремовый", Swatch = Hex(0xefcd96) },
            new() { Id = "Chocolate", Name = "Шоколадный", Swatch = Hex(0x6e4530) },
            new() { Id = "Leopard", Name = "Пятнистый", Swatch = Hex(0xdda25a) },
            new() { Id = "GingerWhite", Name = "Рыжий с белым", Swatch = Hex(0xee8a2e) },
        };

        public static int Count => All.Length;

        /// <summary>Окраска по умолчанию для цвета игрока (когда сам не выбирал).</summary>
        public static int ForColorIndex(int colorIndex) => colorIndex < 0 ? 0 : colorIndex % Count;

        public static string NameOf(int index) => index < 0 ? "Случайная" : All[index % Count].Name;

        private static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
