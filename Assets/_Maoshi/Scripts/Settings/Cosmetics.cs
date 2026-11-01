using System;
using System.Collections.Generic;
using Maoshi.Cat;
using UnityEngine;

namespace Maoshi.Settings
{
    /// <summary>
    /// Консервные банки и аксессуары игрока — сохраняются между партиями на этом компьютере (PlayerPrefs, JSON).
    /// Банки: 3 за победу в партии (король двора) и по 1 за каждую выигранную номинацию.
    /// </summary>
    public static class Cosmetics
    {
        private const string Key = "maoshi.cosmetics.v1";

        [Serializable]
        private class Data
        {
            public int cans;
            public List<string> owned = new();
            public string head = "";
            public string neck = "";
            public string face = "";
            public int lastAwardedFinal;
        }

        private static Data _data;

        /// <summary>Что-то поменялось (банки, покупка, надетое) — обновить интерфейс.</summary>
        public static event Action Changed;

        private static Data D
        {
            get
            {
                if (_data != null) return _data;
                string json = PlayerPrefs.GetString(Key, "");
                try { _data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json); }
                catch { _data = new Data(); }
                _data ??= new Data();
                _data.owned ??= new List<string>();
                return _data;
            }
        }

        public static int Cans => D.cans;

        public static bool Owns(int index) => CatAccessories.Valid(index) && D.owned.Contains(CatAccessories.All[index].Key);

        /// <summary>Надетый аксессуар в слоте (индекс в CatAccessories.All) или −1.</summary>
        public static int Equipped(AccessorySlot slot)
        {
            string key = slot switch { AccessorySlot.Head => D.head, AccessorySlot.Neck => D.neck, _ => D.face };
            int index = CatAccessories.IndexOf(key);
            return index >= 0 && Owns(index) ? index : -1;
        }

        public static bool TryBuy(int index)
        {
            if (!CatAccessories.Valid(index) || Owns(index)) return false;
            int price = CatAccessories.All[index].Price;
            if (D.cans < price) return false;
            D.cans -= price;
            D.owned.Add(CatAccessories.All[index].Key);
            Save();
            return true;
        }

        /// <summary>Надеть (если куплено) или снять (index −1) в слоте.</summary>
        public static void Equip(AccessorySlot slot, int index)
        {
            if (index >= 0 && (!Owns(index) || CatAccessories.All[index].Slot != slot)) return;
            string key = index >= 0 ? CatAccessories.All[index].Key : "";
            switch (slot)
            {
                case AccessorySlot.Head: D.head = key; break;
                case AccessorySlot.Neck: D.neck = key; break;
                default: D.face = key; break;
            }
            Save();
        }

        /// <summary>Начислить банки за финал партии (один раз на финал: finalId — номер финала от сервера).</summary>
        public static bool Award(int finalId, int cans)
        {
            if (cans <= 0 || finalId == 0 || D.lastAwardedFinal == finalId) return false;
            D.lastAwardedFinal = finalId;
            D.cans += cans;
            Save();
            return true;
        }

        /// <summary>Только для проверки в редакторе.</summary>
        public static void DebugAdd(int cans)
        {
            D.cans = Mathf.Max(0, D.cans + cans);
            Save();
        }

        private static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(D));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
