using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.Network
{
    /// <summary>
    /// «Паспорт» игрока на всю сессию: имя, цвет, готовность.
    /// Это не тело кота! Кот — отдельный объект, который спавнится в каждой сцене (этап 2).
    /// Объект живёт между сценами (DontDestroyOnLoad), его создаёт NGO при подключении клиента.
    /// Все значения пишет только сервер; клиент просит изменения через RPC.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>
        /// Цвета игроков для интерфейса. Индекс цвета = порода кота (см. CatAppearance):
        /// 0 рыжий, 1 чёрный, 2 белый, 3 серый, 4 сомали (песочный), 5 шоколадный, 6 британец (голубой), 7 американский (серебристый).
        /// Индекс уникален в пределах сессии.
        /// </summary>
        public static readonly Color[] Palette =
        {
            new Color(1.00f, 0.55f, 0.15f), // рыжий
            new Color(0.25f, 0.25f, 0.28f), // чёрный
            new Color(0.95f, 0.95f, 0.92f), // белый
            new Color(0.50f, 0.52f, 0.56f), // серый
            new Color(0.85f, 0.62f, 0.38f), // сомали
            new Color(0.45f, 0.30f, 0.20f), // шоколадный
            new Color(0.48f, 0.58f, 0.75f), // британец
            new Color(0.78f, 0.80f, 0.82f)  // американский серебристый
        };

        private static readonly List<NetworkPlayer> AllPlayers = new();

        /// <summary>Все игроки сессии, отсортированные по порядку подключения.</summary>
        public static IReadOnlyList<NetworkPlayer> All => AllPlayers;

        /// <summary>Игрок, которым управляет этот компьютер.</summary>
        public static NetworkPlayer Local { get; private set; }

        /// <summary>Список игроков или их данные изменились (для UI).</summary>
        public static event Action ListChanged;

        public readonly NetworkVariable<FixedString64Bytes> PlayerName = new();
        public readonly NetworkVariable<int> ColorIndex = new(-1);
        public readonly NetworkVariable<bool> IsReady = new();

        /// <summary>Окраска кота (индекс в CatSkins), выбранная игроком в меню. −1 — по цвету игрока.</summary>
        public readonly NetworkVariable<int> Skin = new(-1);

        /// <summary>Окраска, которую надо показать: выбранная или по цвету игрока.</summary>
        public int EffectiveSkin => Skin.Value >= 0 ? Skin.Value : Maoshi.Cat.CatSkins.ForColorIndex(ColorIndex.Value);

        /// <summary>Аксессуары кота (индексы в CatAccessories.All, −1 — ничего). Игрок выбирает их в магазине.</summary>
        public readonly NetworkVariable<int> HeadItem = new(-1);
        public readonly NetworkVariable<int> NeckItem = new(-1);
        public readonly NetworkVariable<int> FaceItem = new(-1);

        /// <summary>Рыба — валюта сессии. Меняет только сервер.</summary>
        public readonly NetworkVariable<int> Fish = new();

        public string DisplayName => PlayerName.Value.ToString();
        public Color Color => ColorIndex.Value >= 0 ? Palette[ColorIndex.Value % Palette.Length] : Color.gray;
        public bool IsHostPlayer => OwnerClientId == NetworkManager.ServerClientId;

        // Сброс статики, если в настройках отключена перезагрузка домена при входе в Play Mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AllPlayers.Clear();
            Local = null;
            ListChanged = null;
        }

        public override void OnNetworkSpawn()
        {
            // Паспорт игрока переживает смену сцен.
            DontDestroyOnLoad(gameObject);

            if (IsServer)
            {
                PlayerName.Value = new FixedString64Bytes(ConnectionManager.Instance.TakePendingName(OwnerClientId));
                ColorIndex.Value = FindFreeColorIndex();
                IsReady.Value = IsHostPlayer; // хост готов всегда: он сам нажимает «Начать»
            }

            if (IsOwner)
            {
                Local = this;
                // Окраска из главного меню (хранится в настройках игрока).
                if (Maoshi.Settings.GameSettings.CatSkin >= 0) SetSkinRpc(Maoshi.Settings.GameSettings.CatSkin);
                // Надетые аксессуары из магазина (сохранены на этом компьютере).
                SetAccessoriesRpc(Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Head),
                    Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Neck),
                    Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Face));
            }

            AllPlayers.Add(this);
            AllPlayers.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

            PlayerName.OnValueChanged += OnNameChanged;
            ColorIndex.OnValueChanged += OnIntChanged;
            IsReady.OnValueChanged += OnBoolChanged;
            Fish.OnValueChanged += OnIntChanged;
            Skin.OnValueChanged += OnIntChanged;
            HeadItem.OnValueChanged += OnIntChanged;
            NeckItem.OnValueChanged += OnIntChanged;
            FaceItem.OnValueChanged += OnIntChanged;

            ListChanged?.Invoke();
        }

        /// <summary>Найти игрока по ID клиента. null, если такого нет.</summary>
        public static NetworkPlayer FindByClientId(ulong clientId)
        {
            foreach (var player in AllPlayers)
            {
                if (player.OwnerClientId == clientId) return player;
            }

            return null;
        }

        /// <summary>Сервер: начислить (или списать, если отрицательно) рыбу. Ниже нуля не уходит.</summary>
        public void AddFish(int amount)
        {
            if (!IsServer) return;
            Fish.Value = Mathf.Max(0, Fish.Value + amount);
        }

        public override void OnNetworkDespawn()
        {
            PlayerName.OnValueChanged -= OnNameChanged;
            ColorIndex.OnValueChanged -= OnIntChanged;
            IsReady.OnValueChanged -= OnBoolChanged;
            Fish.OnValueChanged -= OnIntChanged;
            Skin.OnValueChanged -= OnIntChanged;
            HeadItem.OnValueChanged -= OnIntChanged;
            NeckItem.OnValueChanged -= OnIntChanged;
            FaceItem.OnValueChanged -= OnIntChanged;

            AllPlayers.Remove(this);
            if (Local == this) Local = null;

            ListChanged?.Invoke();
        }

        /// <summary>Переключить «Готов». Вызывать только у своего игрока.</summary>
        public void ToggleReady()
        {
            if (!IsOwner) return;
            SetReadyRpc(!IsReady.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetReadyRpc(bool ready)
        {
            // После старта игры готовность не меняется.
            if (ConnectionManager.Instance != null && ConnectionManager.Instance.GameStarted) return;
            IsReady.Value = ready;
        }

        /// <summary>Сменить окраску своего кота (можно и во время игры).</summary>
        public void RequestSkin(int skin)
        {
            if (!IsOwner) return;
            SetSkinRpc(skin);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetSkinRpc(int skin)
        {
            Skin.Value = skin < 0 ? -1 : Mathf.Clamp(skin, 0, Maoshi.Cat.CatSkins.Count - 1);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetAccessoriesRpc(int head, int neck, int face)
        {
            // Проверяем только, что слот подходит: покупки хранятся у игрока, это косметика.
            HeadItem.Value = Fits(head, Maoshi.Cat.AccessorySlot.Head);
            NeckItem.Value = Fits(neck, Maoshi.Cat.AccessorySlot.Neck);
            FaceItem.Value = Fits(face, Maoshi.Cat.AccessorySlot.Face);
        }

        private static int Fits(int index, Maoshi.Cat.AccessorySlot slot) =>
            Maoshi.Cat.CatAccessories.Valid(index) && Maoshi.Cat.CatAccessories.All[index].Slot == slot ? index : -1;

        /// <summary>Сервер выбирает первый свободный цвет.</summary>
        private static int FindFreeColorIndex()
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                bool taken = false;
                foreach (var player in AllPlayers)
                {
                    if (player.ColorIndex.Value == i)
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken) return i;
            }

            return 0;
        }

        private void OnNameChanged(FixedString64Bytes oldValue, FixedString64Bytes newValue) => ListChanged?.Invoke();
        private void OnIntChanged(int oldValue, int newValue) => ListChanged?.Invoke();
        private void OnBoolChanged(bool oldValue, bool newValue) => ListChanged?.Invoke();
    }
}
