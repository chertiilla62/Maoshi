using System.Collections.Generic;
using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>Куда надевается аксессуар: в каждом слоте — один.</summary>
    public enum AccessorySlot
    {
        Head,
        Neck,
        Face
    }

    /// <summary>
    /// Каталог аксессуаров котов. Сами модели собраны из простых форм и лежат в префабе кота
    /// (выключенные объекты «Acc_ключ» на костях головы и шеи — их строит Stage 36).
    /// Индекс в All — то, что передаётся по сети (NetworkPlayer.Head/Neck/FaceItem), −1 — ничего.
    /// </summary>
    public static class CatAccessories
    {
        public struct Info
        {
            public string Key;
            public string Name;
            public AccessorySlot Slot;
            public int Price;
            public Color Swatch;
        }

        public static readonly Info[] All =
        {
            new() { Key = "TopHat", Name = "Цилиндр", Slot = AccessorySlot.Head, Price = 6, Swatch = new Color(0.15f, 0.13f, 0.17f) },
            new() { Key = "Crown", Name = "Корона", Slot = AccessorySlot.Head, Price = 10, Swatch = new Color(1f, 0.8f, 0.2f) },
            new() { Key = "Cap", Name = "Кепка", Slot = AccessorySlot.Head, Price = 3, Swatch = new Color(0.2f, 0.45f, 0.85f) },
            new() { Key = "PartyHat", Name = "Колпак", Slot = AccessorySlot.Head, Price = 4, Swatch = new Color(0.95f, 0.35f, 0.6f) },
            new() { Key = "Bow", Name = "Бантик", Slot = AccessorySlot.Head, Price = 3, Swatch = new Color(1f, 0.45f, 0.65f) },
            new() { Key = "Cowboy", Name = "Ковбойская шляпа", Slot = AccessorySlot.Head, Price = 7, Swatch = new Color(0.6f, 0.38f, 0.2f) },
            new() { Key = "Collar", Name = "Ошейник с колокольчиком", Slot = AccessorySlot.Neck, Price = 2, Swatch = new Color(0.85f, 0.15f, 0.15f) },
            new() { Key = "Scarf", Name = "Шарф", Slot = AccessorySlot.Neck, Price = 4, Swatch = new Color(0.25f, 0.6f, 0.4f) },
            new() { Key = "BowTie", Name = "Бабочка", Slot = AccessorySlot.Neck, Price = 5, Swatch = new Color(0.2f, 0.2f, 0.25f) },
            new() { Key = "Glasses", Name = "Очки", Slot = AccessorySlot.Face, Price = 3, Swatch = new Color(0.55f, 0.35f, 0.15f) },
            new() { Key = "Shades", Name = "Тёмные очки", Slot = AccessorySlot.Face, Price = 5, Swatch = new Color(0.1f, 0.1f, 0.12f) },
            new() { Key = "Patch", Name = "Повязка пирата", Slot = AccessorySlot.Face, Price = 4, Swatch = new Color(0.12f, 0.1f, 0.1f) },

            // ── С приколом ──
            new() { Key = "TinFoil", Name = "Шапочка из фольги", Slot = AccessorySlot.Head, Price = 5, Swatch = new Color(0.78f, 0.8f, 0.84f) },
            new() { Key = "Halo", Name = "Нимб святого кота", Slot = AccessorySlot.Head, Price = 8, Swatch = new Color(1f, 0.92f, 0.45f) },
            new() { Key = "Horns", Name = "Рожки чертёнка", Slot = AccessorySlot.Head, Price = 6, Swatch = new Color(0.85f, 0.12f, 0.1f) },
            new() { Key = "Propeller", Name = "Кепка с пропеллером", Slot = AccessorySlot.Head, Price = 7, Swatch = new Color(0.3f, 0.7f, 0.95f) },
            new() { Key = "Bucket", Name = "Ведро на голове", Slot = AccessorySlot.Head, Price = 3, Swatch = new Color(0.6f, 0.64f, 0.7f) },
            new() { Key = "Cucumber", Name = "Огурец (страшно!)", Slot = AccessorySlot.Head, Price = 4, Swatch = new Color(0.3f, 0.6f, 0.25f) },
            new() { Key = "PixelShades", Name = "Пиксельные очки", Slot = AccessorySlot.Face, Price = 8, Swatch = new Color(0.05f, 0.05f, 0.05f) },
            new() { Key = "Mustache", Name = "Усы джентльмена", Slot = AccessorySlot.Face, Price = 4, Swatch = new Color(0.15f, 0.1f, 0.08f) },
            new() { Key = "ClownNose", Name = "Нос клоуна", Slot = AccessorySlot.Face, Price = 2, Swatch = new Color(0.95f, 0.15f, 0.15f) },
            new() { Key = "Monocle", Name = "Монокль", Slot = AccessorySlot.Face, Price = 6, Swatch = new Color(0.95f, 0.78f, 0.25f) },
            new() { Key = "Bread", Name = "Кот в хлебушке", Slot = AccessorySlot.Face, Price = 9, Swatch = new Color(0.85f, 0.6f, 0.3f) },
            new() { Key = "GoldChain", Name = "Золотая цепь", Slot = AccessorySlot.Neck, Price = 9, Swatch = new Color(1f, 0.8f, 0.2f) },
            new() { Key = "Tie", Name = "Офисный галстук", Slot = AccessorySlot.Neck, Price = 3, Swatch = new Color(0.8f, 0.15f, 0.2f) },
            new() { Key = "Cape", Name = "Плащ супергероя", Slot = AccessorySlot.Neck, Price = 7, Swatch = new Color(0.85f, 0.1f, 0.15f) },
            new() { Key = "Cone", Name = "Воротник позора", Slot = AccessorySlot.Neck, Price = 5, Swatch = new Color(0.9f, 0.95f, 1f) },
            new() { Key = "Medal", Name = "Медаль «Лучший кот»", Slot = AccessorySlot.Neck, Price = 4, Swatch = new Color(0.95f, 0.75f, 0.2f) },
        };

        public static string SlotName(AccessorySlot slot) => slot switch
        {
            AccessorySlot.Head => "Голова",
            AccessorySlot.Neck => "Шея",
            _ => "Морда"
        };

        public static int IndexOf(string key)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Key == key) return i;
            return -1;
        }

        public static bool Valid(int index) => index >= 0 && index < All.Length;

        /// <summary>
        /// Включить в модели кота нужные аксессуары и выключить остальные (объекты «Acc_ключ» в модели).
        /// cache — найденные объекты по ключу (заполняется при первом вызове).
        /// </summary>
        public static void ApplyTo(Transform model, Dictionary<string, GameObject> cache, int head, int neck, int face)
        {
            if (model == null) return;
            if (cache.Count == 0)
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Acc_")) cache[t.name.Substring(4)] = t.gameObject;

            foreach (var pair in cache)
            {
                int index = IndexOf(pair.Key);
                bool on = index >= 0 && (index == head || index == neck || index == face);
                if (pair.Value != null && pair.Value.activeSelf != on) pair.Value.SetActive(on);
            }
        }
    }
}
