using System;
using System.Collections.Generic;
using Maoshi.Network;
using Maoshi.Session;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Maoshi.Showdown
{
    /// <summary>Этап свалки.</summary>
    public enum ShowdownStage : byte
    {
        Inactive,
        /// <summary>Все тайно выбирают карту и цель.</summary>
        Picking,
        /// <summary>Карты вскрыты, показываем, что случилось.</summary>
        Reveal,
        /// <summary>Свалка окончена, объявлен победитель.</summary>
        Finished
    }

    /// <summary>Публичное состояние бойца (видят все).</summary>
    public struct FighterState : INetworkSerializable, IEquatable<FighterState>
    {
        public ulong ClientId;
        public byte Lives;
        public bool Alive;
        public bool Picked;
        public byte Fleas;
        public bool BoxLocked;
        public ulong AllyId;
        public byte AllyTurns;
        /// <summary>Вскрытая карта (во время Reveal) и её цель.</summary>
        public byte Shown;
        public ulong ShownTarget;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Lives);
            serializer.SerializeValue(ref Alive);
            serializer.SerializeValue(ref Picked);
            serializer.SerializeValue(ref Fleas);
            serializer.SerializeValue(ref BoxLocked);
            serializer.SerializeValue(ref AllyId);
            serializer.SerializeValue(ref AllyTurns);
            serializer.SerializeValue(ref Shown);
            serializer.SerializeValue(ref ShownTarget);
        }

        public bool Equals(FighterState other) =>
            ClientId == other.ClientId && Lives == other.Lives && Alive == other.Alive && Picked == other.Picked &&
            Fleas == other.Fleas && BoxLocked == other.BoxLocked && AllyId == other.AllyId && AllyTurns == other.AllyTurns &&
            Shown == other.Shown && ShownTarget == other.ShownTarget;

        public ShowdownCard ShownCard => (ShowdownCard)Shown;
    }

    /// <summary>
    /// «Кошачья свалка»: карточная драка всех претендентов на зону (2–8 котов).
    /// Каждый ход все одновременно и тайно выбирают карту и цель, потом карты вскрываются разом
    /// и разрешаются по BrawlRules. Последний кот с жизнями (или лучший через 8 ходов) забирает зону.
    /// Руки и колоды хранит сервер; каждый боец получает только свою руку личным RPC.
    /// Зрители и выбывшие один раз за свалку могут подкинуть бойцу случайную карту.
    /// Живёт на объекте SessionManager (между сценами). Имя класса осталось от старой «разборки».
    /// </summary>
    public class ShowdownManager : NetworkBehaviour
    {
        /// <summary>ID ботов для тренировки (G во дворе в редакторе).</summary>
        public const ulong BotIdBase = ulong.MaxValue - 100;
        private static readonly string[] BotNames = { "Бот-Мурзик", "Бот-Барсик", "Бот-Пушок", "Бот-Васька" };

        public static ShowdownManager Instance { get; private set; }

        [SerializeField] private float pickDuration = 12f;
        [SerializeField] private float revealDuration = 4f;
        [SerializeField] private float finishedDuration = 3.5f;
        [Tooltip("Сколько ботов добавить в тренировочную свалку (клавиша G).")]
        [SerializeField] private int practiceBots = 3;

        public readonly NetworkVariable<ShowdownStage> Stage = new(ShowdownStage.Inactive);
        public readonly NetworkVariable<int> Round = new();
        public readonly NetworkVariable<int> ZoneId = new(-1);
        public readonly NetworkVariable<double> StageEndTime = new();
        public readonly NetworkVariable<ulong> Winner = new(BrawlRules.NoOne);
        public readonly NetworkVariable<FixedString128Bytes> GiftText = new();
        public NetworkList<FighterState> Fighters;
        /// <summary>Журнал последнего хода (что случилось при вскрытии).</summary>
        public NetworkList<FixedString128Bytes> Log;
        public NetworkList<ulong> GiftsUsed;

        /// <summary>Рука локального игрока (если он боец).</summary>
        public IReadOnlyList<ShowdownCard> LocalHand => _localHand;

        public bool IsActive => Stage.Value != ShowdownStage.Inactive;
        public float TimeLeft => Mathf.Max(0f, (float)(StageEndTime.Value - Now));

        private readonly List<ShowdownCard> _localHand = new();

        // Только сервер.
        private readonly List<BrawlFighter> _fighters = new();
        private readonly List<BrawlFighter> _diedLastTurn = new();
        private Action<ulong> _onFinished;

        private double Now => NetworkManager.ServerTime.Time;

        private void Awake()
        {
            Fighters = new NetworkList<FighterState>();
            Log = new NetworkList<FixedString128Bytes>();
            GiftsUsed = new NetworkList<ulong>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Stage.OnValueChanged += OnStageChanged;
        }

        public override void OnNetworkDespawn()
        {
            Stage.OnValueChanged -= OnStageChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>Имя участника (с учётом ботов).</summary>
        public static string NameOf(ulong clientId)
        {
            if (IsBot(clientId))
            {
                int index = (int)(clientId - BotIdBase) % BotNames.Length;
                return BotNames[index];
            }

            var player = NetworkPlayer.FindByClientId(clientId);
            return player != null ? player.DisplayName : "ушёл";
        }

        public static bool IsBot(ulong clientId) => clientId >= BotIdBase && clientId < BotIdBase + 50;

        /// <summary>Публичное состояние бойца по ID (false — он не участвует).</summary>
        public bool TryGetFighter(ulong clientId, out FighterState state)
        {
            foreach (var f in Fighters)
            {
                if (f.ClientId != clientId) continue;
                state = f;
                return true;
            }

            state = default;
            return false;
        }

        // ───────────────────────── Сервер: старт ─────────────────────────

        /// <summary>
        /// Сервер: начать свалку. bids — ставки бойцов (с бонусом владельца) для ничьей.
        /// onFinished получает ID победителя.
        /// </summary>
        public void Begin(IReadOnlyList<ulong> fighters, int zoneId, IReadOnlyDictionary<ulong, int> bids, Action<ulong> onFinished)
        {
            if (!IsServer || IsActive || fighters.Count < 2) return;

            _onFinished = onFinished;
            _fighters.Clear();
            foreach (ulong id in fighters)
            {
                var fighter = new BrawlFighter
                {
                    Id = id,
                    Name = NameOf(id),
                    Lives = BrawlRules.StartLives,
                    Bid = bids != null && bids.TryGetValue(id, out int bid) ? bid : 0,
                };
                fighter.Deck.AddRange(BrawlRules.BuildDeck());
                Shuffle(fighter.Deck);
                DrawUpTo(fighter, BrawlRules.HandSize);
                _fighters.Add(fighter);
            }

            Round.Value = 1;
            ZoneId.Value = zoneId;
            Winner.Value = BrawlRules.NoOne;
            GiftText.Value = default;
            GiftsUsed.Clear();
            SetLog(new List<string> { $"Свалка! Котов: {fighters.Count}. Выбирайте карты" });

            foreach (var fighter in _fighters) SyncHand(fighter);
            StartPicking();
        }

        /// <summary>Сервер, только редактор: тренировочная свалка хоста с ботами (без зоны).</summary>
        public void DebugStartVsBots()
        {
            var ids = new List<ulong> { NetworkManager.LocalClientId };
            for (int i = 0; i < Mathf.Clamp(practiceBots, 1, 7); i++) ids.Add(BotIdBase + (ulong)i);
            Begin(ids, -1, null, null);
        }

        private void StartPicking()
        {
            foreach (var f in _fighters) f.HasPick = false;
            StageEndTime.Value = Now + pickDuration;
            Stage.Value = ShowdownStage.Picking;
            PublishFighters(false);
        }

        // ───────────────────────── Сервер: цикл ─────────────────────────

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (Application.isEditor) DebugHotkey();

            switch (Stage.Value)
            {
                case ShowdownStage.Picking:
                    foreach (var f in _fighters)
                        if (f.Alive && !f.HasPick && IsBot(f.Id)) BotPick(f);

                    bool everyonePicked = true;
                    foreach (var f in _fighters)
                        if (f.Alive && !f.HasPick) everyonePicked = false;

                    if (everyonePicked || Now >= StageEndTime.Value) ResolveRound();
                    break;

                case ShowdownStage.Reveal:
                    if (Now < StageEndTime.Value) break;
                    if (AliveCount() <= 1 || Round.Value >= BrawlRules.MaxTurns) Finish();
                    else NextTurn();
                    break;

                case ShowdownStage.Finished:
                    if (Now < StageEndTime.Value) break;
                    Stage.Value = ShowdownStage.Inactive;
                    var callback = _onFinished;
                    _onFinished = null;
                    callback?.Invoke(Winner.Value);
                    break;
            }
        }

        private void ResolveRound()
        {
            var log = new List<string>();
            var aliveBefore = new List<BrawlFighter>();
            foreach (var f in _fighters) if (f.Alive) aliveBefore.Add(f);

            // Сыгранные карты уходят из руки.
            foreach (var f in aliveBefore)
                if (f.HasPick) f.Hand.Remove(f.Pick.Card);

            BrawlRules.ResolveTurn(_fighters, log);

            _diedLastTurn.Clear();
            foreach (var f in aliveBefore) if (!f.Alive) _diedLastTurn.Add(f);

            SetLog(log);
            StageEndTime.Value = Now + revealDuration;
            Stage.Value = ShowdownStage.Reveal;
            PublishFighters(true);
        }

        private void NextTurn()
        {
            Round.Value++;

            // Добор до 5 и блохи — в начале нового хода.
            foreach (var f in _fighters)
            {
                if (!f.Alive) continue;
                DrawUpTo(f, BrawlRules.HandSize);
                SyncHand(f);
            }

            var fleaLog = new List<string>();
            var aliveBefore = new List<BrawlFighter>();
            foreach (var f in _fighters) if (f.Alive) aliveBefore.Add(f);
            BrawlRules.TickFleas(_fighters, fleaLog);

            if (fleaLog.Count > 0)
            {
                _diedLastTurn.Clear();
                foreach (var f in aliveBefore) if (!f.Alive) _diedLastTurn.Add(f);

                // Блохи могли решить исход.
                if (AliveCount() <= 1)
                {
                    SetLog(fleaLog);
                    PublishFighters(false);
                    Finish();
                    return;
                }

                fleaLog.Add($"Ход {Round.Value}. Выбирайте карты!");
                SetLog(fleaLog);
            }
            else
            {
                SetLog(new List<string> { $"Ход {Round.Value}. Выбирайте карты!" });
            }

            StartPicking();
        }

        private void Finish()
        {
            ulong winner = BrawlRules.PickWinner(_fighters, _diedLastTurn, Random.value);
            Winner.Value = winner;

            var lines = new List<string>();
            foreach (var line in Log) lines.Add(line.ToString());
            lines.Add(winner == BrawlRules.NoOne ? "Победителя нет" : $"Свалку выиграл {NameOf(winner)}!");
            SetLog(lines);

            StageEndTime.Value = Now + finishedDuration;
            Stage.Value = ShowdownStage.Finished;
            PublishFighters(true);
        }

        private int AliveCount()
        {
            int count = 0;
            foreach (var f in _fighters) if (f.Alive) count++;
            return count;
        }

        private void DebugHotkey()
        {
            // G во дворе во время голосования — тренировочная свалка с ботами.
            var keyboard = Maoshi.GameKeyboard.Current;
            var session = SessionManager.Instance;
            if (keyboard == null || session == null || IsActive) return;
            if (session.Phase.Value != SessionPhase.Hub || Hub.HubVoting.Current == null) return;
            if (keyboard.gKey.wasPressedThisFrame) DebugStartVsBots();
        }

        // ───────────────────────── Боты ─────────────────────────

        /// <summary>Простой бот: бьёт лидера, при малом здоровье лечится или защищается.</summary>
        private void BotPick(BrawlFighter bot)
        {
            BrawlFighter leader = null;
            foreach (var f in _fighters)
            {
                if (!f.Alive || f.Id == bot.Id || bot.IsAlliedWith(f.Id)) continue;
                if (leader == null || f.Lives > leader.Lives || (f.Lives == leader.Lives && Random.value < 0.5f)) leader = f;
            }

            var options = new List<BrawlPick>();
            foreach (var card in bot.Hand)
            {
                var info = BrawlRules.Info(card);
                if (info.NeedsTarget && leader == null) continue;
                var pick = new BrawlPick { Card = card, Target = info.NeedsTarget ? leader.Id : BrawlRules.NoOne };
                if (!BrawlRules.IsValidPick(bot, pick, _fighters)) continue;

                // Веса: раненым — лечение и защита, здоровым — атака.
                int weight = info.Type switch
                {
                    CardType.Attack => bot.Lives >= 3 ? 4 : 2,
                    CardType.Defense => bot.Lives <= 2 ? 4 : 1,
                    CardType.Care => bot.Lives < BrawlRules.StartLives ? (bot.Lives <= 2 ? 5 : 2) : 0,
                    _ => 2,
                };
                for (int i = 0; i < weight; i++) options.Add(pick);
            }

            bot.Pick = options.Count > 0 ? options[Random.Range(0, options.Count)] : new BrawlPick { Card = ShowdownCard.None, Target = BrawlRules.NoOne };
            bot.HasPick = true; // если подходящих карт нет — пропуск хода
            PublishFighters(false);
        }

        // ───────────────────────── RPC: выбор и подарки ─────────────────────────

        /// <summary>Клиент-боец: сыграть карту из руки (номер в руке) на цель (или NoOne).</summary>
        public void PickCard(int handIndex, ulong target) => PickCardRpc(handIndex, target);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PickCardRpc(int handIndex, ulong target, RpcParams rpcParams = default)
        {
            if (Stage.Value != ShowdownStage.Picking) return;

            var fighter = FindFighter(rpcParams.Receive.SenderClientId);
            if (fighter == null || !fighter.Alive || fighter.HasPick) return;
            if (handIndex < 0 || handIndex >= fighter.Hand.Count) return;

            var card = fighter.Hand[handIndex];
            var pick = new BrawlPick { Card = card, Target = BrawlRules.Info(card).NeedsTarget ? target : BrawlRules.NoOne };
            if (!BrawlRules.IsValidPick(fighter, pick, _fighters)) return;

            fighter.Pick = pick;
            fighter.HasPick = true;
            PublishFighters(false);
        }

        /// <summary>Клиент-зритель или выбывший: подкинуть бойцу случайную карту (раз за свалку).</summary>
        public void Gift(ulong toFighter) => GiftRpc(toFighter);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void GiftRpc(ulong toFighter, RpcParams rpcParams = default)
        {
            if (Stage.Value != ShowdownStage.Picking && Stage.Value != ShowdownStage.Reveal) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            var senderFighter = FindFighter(sender);
            if (senderFighter != null && senderFighter.Alive) return; // живые бойцы не дарят
            if (GiftsUsed.Contains(sender)) return;

            var target = FindFighter(toFighter);
            if (target == null || !target.Alive || target.Hand.Count >= BrawlRules.MaxHandSize) return;

            var deck = BrawlRules.BuildDeck();
            target.Hand.Add(deck[Random.Range(0, deck.Count)]);
            GiftsUsed.Add(sender);
            GiftText.Value = new FixedString128Bytes(BrawlRules.FitBytes($"{NameOf(sender)} подкинул карту: {target.Name}"));
            SyncHand(target);
        }

        // ───────────────────────── Синхронизация ─────────────────────────

        private BrawlFighter FindFighter(ulong id)
        {
            foreach (var f in _fighters) if (f.Id == id) return f;
            return null;
        }

        /// <summary>Сервер → все: публичное состояние бойцов. reveal — показать сыгранные карты.</summary>
        private void PublishFighters(bool reveal)
        {
            for (int i = 0; i < _fighters.Count; i++)
            {
                var f = _fighters[i];
                bool played = reveal && f.HasPick && f.Pick.Card != ShowdownCard.None;
                var state = new FighterState
                {
                    ClientId = f.Id,
                    Lives = (byte)Mathf.Clamp(f.Lives, 0, 255),
                    Alive = f.Alive,
                    Picked = f.HasPick,
                    Fleas = (byte)f.Fleas,
                    BoxLocked = f.BoxLocked,
                    AllyId = f.AllyTurns > 0 ? f.AllyId : BrawlRules.NoOne,
                    AllyTurns = (byte)Mathf.Max(0, f.AllyTurns),
                    Shown = played ? (byte)f.Pick.Card : (byte)0,
                    ShownTarget = played ? f.Pick.Target : BrawlRules.NoOne,
                };

                if (i < Fighters.Count)
                {
                    if (!Fighters[i].Equals(state)) Fighters[i] = state;
                }
                else Fighters.Add(state);
            }

            while (Fighters.Count > _fighters.Count) Fighters.RemoveAt(Fighters.Count - 1);
        }

        private void SetLog(List<string> lines)
        {
            Log.Clear();
            // Не больше 12 строк — иначе окно не влезет.
            int start = Mathf.Max(0, lines.Count - 12);
            for (int i = start; i < lines.Count; i++)
                Log.Add(new FixedString128Bytes(BrawlRules.FitBytes(lines[i])));
        }

        private void SyncHand(BrawlFighter fighter)
        {
            if (IsBot(fighter.Id) || NetworkPlayer.FindByClientId(fighter.Id) == null) return;

            var bytes = new byte[fighter.Hand.Count];
            for (int i = 0; i < fighter.Hand.Count; i++) bytes[i] = (byte)fighter.Hand[i];
            HandRpc(bytes, RpcTarget.Single(fighter.Id, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void HandRpc(byte[] cards, RpcParams rpcParams)
        {
            _localHand.Clear();
            foreach (byte card in cards) _localHand.Add((ShowdownCard)card);
        }

        private void OnStageChanged(ShowdownStage previous, ShowdownStage next)
        {
            if (next == ShowdownStage.Inactive) _localHand.Clear();
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        private static void DrawUpTo(BrawlFighter fighter, int count)
        {
            while (fighter.Hand.Count < count)
            {
                // Колода кончилась — замешиваем новую.
                if (fighter.Deck.Count == 0)
                {
                    fighter.Deck.AddRange(BrawlRules.BuildDeck());
                    Shuffle(fighter.Deck);
                }

                fighter.Hand.Add(fighter.Deck[fighter.Deck.Count - 1]);
                fighter.Deck.RemoveAt(fighter.Deck.Count - 1);
            }
        }

        private static void Shuffle(List<ShowdownCard> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
