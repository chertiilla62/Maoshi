using System;
using System.Collections.Generic;
using System.Text;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.Hub
{
    /// <summary>На что ставят в кошачьей рулетке.</summary>
    public enum RouletteKind : byte
    {
        Orange = 0,
        Gold = 1,
        Gray = 2,
        Dog = 3
    }

    public enum RouletteState : byte
    {
        /// <summary>Колесо стоит, ставок нет.</summary>
        Idle,
        /// <summary>Первая ставка открыла приём ставок, идёт отсчёт.</summary>
        Betting,
        /// <summary>Колесо крутится, результат уже известен серверу.</summary>
        Spinning,
        /// <summary>Выплаты сделаны, показываем, что выпало.</summary>
        Result
    }

    /// <summary>Ставка игрока на один цвет в текущем круге.</summary>
    public struct RouletteBet : INetworkSerializable, IEquatable<RouletteBet>
    {
        public ulong Client;
        public byte Kind;
        public int Amount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Client);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Amount);
        }

        public bool Equals(RouletteBet other) => Client == other.Client && Kind == other.Kind && Amount == other.Amount;
    }

    /// <summary>
    /// Кошачья рулетка во дворе. Колесо на 12 секторов: 5 рыжих (×2), 5 серых (×2), золотая рыбка (×10) и пёс (забирает всё).
    /// Ставят, стоя котом на коврике перед колесом: E — +1 рыба (Shift+E — +5), Q — забрать 1 (Shift+Q — всё с этого коврика).
    /// Первая ставка открывает приём ставок на несколько секунд, потом колесо крутится и платит.
    ///
    /// Всё решает сервер: рыба списывается сразу при ставке, сектор выбирается случайно в момент раскрутки,
    /// выплата — когда колесо остановилось. Клиенты по сетевому времени и итоговому углу одинаково анимируют колесо.
    /// Если голосование закончилось во время приёма ставок — колесо крутится сразу, а загрузка мини-игры ждёт выплат.
    /// </summary>
    public class CatRoulette : NetworkBehaviour
    {
        public static CatRoulette Current { get; private set; }

        /// <summary>Сектора по часовой стрелке от верха (как на текстуре RouletteWheel).</summary>
        public static readonly RouletteKind[] Sectors =
        {
            RouletteKind.Gold, RouletteKind.Orange, RouletteKind.Gray, RouletteKind.Orange, RouletteKind.Gray, RouletteKind.Orange,
            RouletteKind.Dog, RouletteKind.Gray, RouletteKind.Orange, RouletteKind.Gray, RouletteKind.Orange, RouletteKind.Gray
        };

        private const float SectorAngle = 30f;

        [Header("Сцена")]
        [SerializeField] private Transform wheel;
        [SerializeField] private Transform pointer;
        [Tooltip("Коврики для ставок по порядку: рыжие, золотая, серые.")]
        [SerializeField] private Transform[] mats = new Transform[3];
        [SerializeField] private Vector2 matHalfSize = new(0.8f, 0.85f);
        [SerializeField] private TextMesh[] matLabels = new TextMesh[3];
        [SerializeField] private TextMesh statusLabel;
        [SerializeField] private Renderer[] bulbs;

        [Header("Правила")]
        [SerializeField] private float bettingSeconds = 5f;
        [SerializeField] private float spinSeconds = 6f;
        [SerializeField] private float resultSeconds = 4f;
        [SerializeField] private int maxBetPerRound = 10;
        [Tooltip("Ставки не принимаются, если до конца голосования осталось меньше.")]
        [SerializeField] private float closeBeforeVoteEnd = 10f;

        public readonly NetworkVariable<RouletteState> State = new(RouletteState.Idle);
        public readonly NetworkVariable<double> StateEnd = new();
        public readonly NetworkVariable<int> ResultSector = new(-1);
        public readonly NetworkVariable<double> SpinStart = new();
        public readonly NetworkVariable<float> SpinFrom = new();
        public readonly NetworkVariable<float> SpinTo = new();
        public NetworkList<RouletteBet> Bets;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _block;
        private Quaternion _wheelRest;
        private Quaternion _pointerRest;
        private int _lastPeg = int.MinValue;
        private float _flap;
        private bool _suppressingVoice;

        /// <summary>Последнее сообщение для локального игрока (нет рыбы, лимит и т. п.).</summary>
        public string Notice { get; private set; } = "";
        public float NoticeUntil { get; private set; }

        private double Now => NetworkManager.ServerTime.Time;

        public float TimeLeft => Mathf.Max(0f, (float)(StateEnd.Value - Now));

        /// <summary>Сервер: пока колесо крутится (или ставки ждут раскрутки), мини-игру не загружаем.</summary>
        public bool HoldsHub => State.Value == RouletteState.Spinning || (State.Value == RouletteState.Betting && Bets.Count > 0);

        public static int Multiplier(RouletteKind kind) => kind switch
        {
            RouletteKind.Gold => 10,
            RouletteKind.Orange => 2,
            RouletteKind.Gray => 2,
            _ => 0
        };

        public static string NameOf(RouletteKind kind) => kind switch
        {
            RouletteKind.Orange => "Рыжие",
            RouletteKind.Gray => "Серые",
            RouletteKind.Gold => "Золотая рыбка",
            _ => "Пёс"
        };

        private void Awake()
        {
            Bets = new NetworkList<RouletteBet>();
            if (wheel != null) _wheelRest = wheel.localRotation;
            if (pointer != null) _pointerRest = pointer.localRotation;
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            State.OnValueChanged += OnStateChanged;
            Bets.OnListChanged += OnBetsChanged;
            RefreshMatLabels();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                // Сцену выгружают посреди круга: ставки возвращаем, а уже крутящееся колесо доигрываем сразу.
                if (State.Value == RouletteState.Betting) RefundAll();
                else if (State.Value == RouletteState.Spinning) Payout();
            }

            State.OnValueChanged -= OnStateChanged;
            Bets.OnListChanged -= OnBetsChanged;
            if (_suppressingVoice) CatVoice.Suppressed = false;
            _suppressingVoice = false;
            if (Current == this) Current = null;
        }

        public override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        // ───────────────────────── Сервер ─────────────────────────

        /// <summary>Открыта ли рулетка для ставок (сервер и клиенты считают одинаково).</summary>
        public bool AcceptsBets(out string reason)
        {
            reason = "";
            var session = SessionManager.Instance;
            var voting = HubVoting.Current;
            var duel = Showdown.ShowdownManager.Instance;
            if (session == null || session.Phase.Value != SessionPhase.Hub || voting == null || voting.Phase.Value != VotePhase.Voting)
            {
                reason = "Рулетка закрыта — выбираем игру";
                return false;
            }
            if (duel != null && duel.IsActive)
            {
                reason = "Рулетка ждёт конца разборки";
                return false;
            }
            if (State.Value == RouletteState.Spinning || State.Value == RouletteState.Result)
            {
                reason = "Колесо крутится — ставки на следующий круг";
                return false;
            }
            if (voting.TimeLeft < closeBeforeVoteEnd)
            {
                reason = $"Ставки закрыты: до игры меньше {Mathf.RoundToInt(closeBeforeVoteEnd)} с";
                return false;
            }
            return true;
        }

        [Rpc(SendTo.Server)]
        private void BetRpc(byte kind, int delta, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (kind > (byte)RouletteKind.Gray || delta == 0) return;

            if (!AcceptsBets(out string reason))
            {
                NoticeRpc(reason, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }

            var player = NetworkPlayer.FindByClientId(sender);
            var cat = CatController.FindByOwner(sender);
            if (player == null || cat == null || MatAt(cat.transform.position) != kind) return;

            int index = FindBet(sender, kind);
            int current = index >= 0 ? Bets[index].Amount : 0;
            int amount;
            if (delta > 0)
            {
                int room = maxBetPerRound - TotalBetOf(sender);
                amount = Mathf.Min(delta, room, player.Fish.Value);
                if (amount <= 0)
                {
                    NoticeRpc(room <= 0 ? $"Больше {maxBetPerRound} рыб за круг ставить нельзя" : "Нет рыбы — выиграй её в мини-игре",
                        RpcTarget.Single(sender, RpcTargetUse.Temp));
                    return;
                }
                player.AddFish(-amount);
                SessionStats.Add(sender, Stat.RouletteBet, amount);
            }
            else
            {
                amount = -Mathf.Min(-delta, current);
                if (amount == 0) return;
                player.AddFish(-amount);
                SessionStats.Add(sender, Stat.RouletteBet, amount); // забрал ставку — не считается
            }

            int next = current + amount;
            if (index >= 0)
            {
                if (next > 0) Bets[index] = new RouletteBet { Client = sender, Kind = kind, Amount = next };
                else Bets.RemoveAt(index);
            }
            else if (next > 0)
            {
                Bets.Add(new RouletteBet { Client = sender, Kind = kind, Amount = next });
            }

            BetSoundRpc(kind, amount > 0);

            if (State.Value == RouletteState.Idle && Bets.Count > 0)
            {
                // Первая ставка: приём ставок открыт, но не дольше, чем идёт голосование.
                double end = Now + bettingSeconds;
                var voting = HubVoting.Current;
                if (voting != null) end = Math.Min(end, voting.PhaseEndTime.Value - closeBeforeVoteEnd);
                StateEnd.Value = Math.Max(Now + 1.0, end);
                State.Value = RouletteState.Betting;
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void NoticeRpc(string text, RpcParams rpcParams)
        {
            Notice = text;
            NoticeUntil = Time.time + 2.5f;
            AudioManager.Play("ui_back", 0.6f);
        }

        [Rpc(SendTo.Everyone)]
        private void BetSoundRpc(byte kind, bool placed)
        {
            Vector3 at = kind < mats.Length && mats[kind] != null ? mats[kind].position : transform.position;
            AudioManager.PlayAt(placed ? "bet" : "unbet", at, 0.8f, 0.08f, 25f);
        }

        /// <summary>Сервер: голосование закончилось — если есть ставки, крутим прямо сейчас, иначе закрываемся.</summary>
        public void CloseBetting()
        {
            if (!IsServer || State.Value != RouletteState.Betting) return;
            if (Bets.Count > 0) StartSpin();
            else State.Value = RouletteState.Idle;
        }

        private void Update()
        {
            if (IsSpawned && IsServer) ServerTick();
            if (IsSpawned) ClientTick();
        }

        private void ServerTick()
        {
            // Только в редакторе: F9 — хосту 10 рыб, чтобы проверить рулетку без мини-игр.
            if (Application.isEditor && Maoshi.GameKeyboard.Current != null && Maoshi.GameKeyboard.Current.f9Key.wasPressedThisFrame)
                NetworkPlayer.FindByClientId(NetworkManager.LocalClientId)?.AddFish(10);
            // F10 — свой кот на коврик золотой рыбки (лицом к колесу).
            if (Application.isEditor && Maoshi.GameKeyboard.Current != null && Maoshi.GameKeyboard.Current.f10Key.wasPressedThisFrame && mats[1] != null)
                CatController.FindByOwner(NetworkManager.LocalClientId)?.SetRespawnAndTeleportRpc(mats[1].position + Vector3.up * 0.05f, Quaternion.LookRotation(-mats[1].forward));

            switch (State.Value)
            {
                case RouletteState.Betting:
                    // Ставки закрываются за closeBeforeVoteEnd до игры (в том числе если все проголосовали раньше).
                    var voting = HubVoting.Current;
                    bool closing = voting != null && voting.Phase.Value == VotePhase.Voting && voting.TimeLeft < closeBeforeVoteEnd;
                    if (Now < StateEnd.Value && !closing) return;
                    if (Bets.Count > 0) StartSpin();
                    else State.Value = RouletteState.Idle;
                    break;

                case RouletteState.Spinning:
                    if (Now < StateEnd.Value) return;
                    Payout();
                    StateEnd.Value = Now + resultSeconds;
                    State.Value = RouletteState.Result;
                    break;

                case RouletteState.Result:
                    if (Now < StateEnd.Value) return;
                    Bets.Clear();
                    State.Value = RouletteState.Idle;
                    break;
            }
        }

        private void StartSpin()
        {
            int sector = UnityEngine.Random.Range(0, Sectors.Length);
            float from = Mathf.Repeat(SpinTo.Value, 360f);
            // Сектор i под язычком (вверху), когда колесо повернуто на −30·i по часовой; добавляем 4–5 оборотов и разброс внутри сектора.
            float target = Mathf.Repeat(-sector * SectorAngle - from, 360f);
            float jitter = UnityEngine.Random.Range(-SectorAngle * 0.36f, SectorAngle * 0.36f);
            int turns = UnityEngine.Random.Range(4, 6);

            ResultSector.Value = sector;
            SpinFrom.Value = from;
            SpinTo.Value = from + turns * 360f + target + jitter;
            SpinStart.Value = Now;
            StateEnd.Value = Now + spinSeconds;
            State.Value = RouletteState.Spinning;
            Debug.Log($"[Maoshi] Рулетка: крутим, выпадет {NameOf(Sectors[sector])} (сектор {sector}), ставок {Bets.Count}.");
        }

        private void Payout()
        {
            int sector = ResultSector.Value;
            if (sector < 0 || sector >= Sectors.Length) return;
            var kind = Sectors[sector];
            foreach (var bet in Bets)
            {
                if ((RouletteKind)bet.Kind != kind) continue;
                var player = NetworkPlayer.FindByClientId(bet.Client);
                if (player != null) player.AddFish(bet.Amount * Multiplier(kind));
                SessionStats.Add(bet.Client, Stat.RouletteWon, bet.Amount * Multiplier(kind));
            }
            Debug.Log($"[Maoshi] Рулетка: выпало {NameOf(kind)}.");
        }

        private void RefundAll()
        {
            foreach (var bet in Bets)
            {
                var player = NetworkPlayer.FindByClientId(bet.Client);
                if (player != null) player.AddFish(bet.Amount);
            }
            Bets.Clear();
        }

        // ───────────────────────── Общие расчёты ─────────────────────────

        /// <summary>На каком коврике точка (-1 — ни на каком).</summary>
        public int MatAt(Vector3 worldPosition)
        {
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Vector3 local = mats[i].InverseTransformPoint(worldPosition);
                if (local.y < -0.5f || local.y > 1.5f) continue;
                if (Mathf.Abs(local.x) <= matHalfSize.x && Mathf.Abs(local.z) <= matHalfSize.y) return i;
            }
            return -1;
        }

        private int FindBet(ulong client, byte kind)
        {
            for (int i = 0; i < Bets.Count; i++)
                if (Bets[i].Client == client && Bets[i].Kind == kind) return i;
            return -1;
        }

        public int TotalBetOf(ulong client)
        {
            int total = 0;
            foreach (var bet in Bets)
                if (bet.Client == client) total += bet.Amount;
            return total;
        }

        public int BetOf(ulong client, RouletteKind kind)
        {
            int i = FindBet(client, (byte)kind);
            return i >= 0 ? Bets[i].Amount : 0;
        }

        /// <summary>Что выпало (после остановки колеса) — иначе null.</summary>
        public RouletteKind? Outcome =>
            State.Value == RouletteState.Result && ResultSector.Value >= 0 && ResultSector.Value < Sectors.Length ? Sectors[ResultSector.Value] : null;

        /// <summary>Сколько рыбы игрок получил в этом круге (0, если проиграл или не ставил).</summary>
        public int WinningsOf(ulong client)
        {
            var outcome = Outcome;
            if (outcome == null) return 0;
            return BetOf(client, outcome.Value) * Multiplier(outcome.Value);
        }

        /// <summary>Угол колеса (градусы по часовой) в момент времени.</summary>
        private float WheelAngle()
        {
            if (State.Value != RouletteState.Spinning) return SpinTo.Value;
            float u = Mathf.Clamp01((float)((Now - SpinStart.Value) / spinSeconds));
            float eased = 1f - Mathf.Pow(1f - u, 3.2f);
            return Mathf.Lerp(SpinFrom.Value, SpinTo.Value, eased);
        }

        // ───────────────────────── Клиент ─────────────────────────

        /// <summary>На каком коврике стоит свой кот (-1 — нигде).</summary>
        public int LocalMat
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return -1;
                var cat = CatController.FindByOwner(nm.LocalClientId);
                return cat != null ? MatAt(cat.transform.position) : -1;
            }
        }

        /// <summary>Расстояние от своего кота до рулетки (для показа панели).</summary>
        public float LocalDistance
        {
            get
            {
                var nm = NetworkManager.Singleton;
                var cat = nm != null ? CatController.FindByOwner(nm.LocalClientId) : null;
                if (cat == null) return float.MaxValue;
                Vector3 d = cat.transform.position - transform.position;
                d.y = 0f;
                return d.magnitude;
            }
        }

        private void ClientTick()
        {
            // Колесо и язычок.
            float angle = WheelAngle();
            if (wheel != null) wheel.localRotation = _wheelRest * Quaternion.Euler(0f, 0f, -angle);
            int peg = Mathf.FloorToInt((angle - SectorAngle * 0.5f) / SectorAngle);
            if (peg != _lastPeg)
            {
                if (_lastPeg != int.MinValue && State.Value == RouletteState.Spinning)
                {
                    _flap = 22f;
                    Vector3 at = pointer != null ? pointer.position : transform.position;
                    AudioManager.PlayAtPitch("tick", at, 0.55f, UnityEngine.Random.Range(0.92f, 1.1f), 25f);
                }
                _lastPeg = peg;
            }
            _flap = Mathf.MoveTowards(_flap, 0f, Time.deltaTime * 160f);
            if (pointer != null) pointer.localRotation = _pointerRest * Quaternion.Euler(0f, 0f, _flap);

            RefreshBulbs();
            RefreshStatusLabel();
            LocalInput();
        }

        private void LocalInput()
        {
            int mat = LocalMat;
            bool onMat = mat >= 0 && !CatInput.Blocked;
            // На коврике E/Q — ставки, а не мяуканье.
            if (onMat != _suppressingVoice)
            {
                CatVoice.Suppressed = onMat;
                _suppressingVoice = onMat;
            }
            if (!onMat) return;

            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard == null) return;
            bool shift = keyboard.shiftKey.isPressed;
            if (keyboard.eKey.wasPressedThisFrame) BetRpc((byte)mat, shift ? 5 : 1);
            else if (keyboard.qKey.wasPressedThisFrame) BetRpc((byte)mat, shift ? -100 : -1);
        }

        private void OnStateChanged(RouletteState previous, RouletteState next)
        {
            switch (next)
            {
                case RouletteState.Betting:
                    AudioManager.PlayAt("bid", transform.position, 0.6f, 0.03f, 30f);
                    break;
                case RouletteState.Spinning:
                    AudioManager.PlayAt("wheel_spin", transform.position, 0.9f, 0.05f, 35f);
                    break;
                case RouletteState.Result:
                    var outcome = Outcome;
                    if (outcome == RouletteKind.Dog)
                    {
                        AudioManager.PlayAt("bark", transform.position, 1f, 0.05f, 35f);
                        AudioManager.PlayAt("lose", transform.position, 0.6f, 0.02f, 35f);
                    }
                    else if (outcome == RouletteKind.Gold) AudioManager.PlayAt("jackpot", transform.position, 1f, 0.02f, 40f);
                    else AudioManager.PlayAt("win", transform.position, 0.6f, 0.02f, 35f);

                    var nm = NetworkManager.Singleton;
                    if (nm != null && WinningsOf(nm.LocalClientId) > 0) AudioManager.Play(outcome == RouletteKind.Gold ? "fish_big" : "fish");
                    break;
            }
            RefreshMatLabels();
        }

        private void OnBetsChanged(NetworkListEvent<RouletteBet> change) => RefreshMatLabels();

        private readonly StringBuilder _sb = new();

        private void RefreshMatLabels()
        {
            var outcome = Outcome;
            for (int i = 0; i < matLabels.Length && i < 3; i++)
            {
                if (matLabels[i] == null) continue;
                var kind = (RouletteKind)i;
                // Название и множитель нарисованы на самом коврике — над ним только ставки.
                _sb.Clear();
                foreach (var bet in Bets)
                {
                    if (bet.Kind != i) continue;
                    if (_sb.Length > 0) _sb.Append('\n');
                    var player = NetworkPlayer.FindByClientId(bet.Client);
                    string name = player != null ? player.DisplayName : "кот";
                    _sb.Append(name).Append(": ").Append(bet.Amount);
                    if (outcome != null) _sb.Append(outcome == kind ? $"  выигрыш +{bet.Amount * Multiplier(kind)}" : "  — мимо");
                }
                matLabels[i].text = _sb.ToString();
            }
        }

        private void RefreshStatusLabel()
        {
            if (statusLabel == null) return;
            string text;
            switch (State.Value)
            {
                case RouletteState.Betting: text = $"Ставки: {Mathf.CeilToInt(TimeLeft)} с"; break;
                case RouletteState.Spinning: text = "Крутим!"; break;
                case RouletteState.Result:
                    var outcome = Outcome;
                    text = outcome == RouletteKind.Dog ? "ПЁС! Всё забрал" : outcome == RouletteKind.Gold ? "ЗОЛОТАЯ РЫБКА ×10!" : $"{NameOf(outcome ?? RouletteKind.Orange)}!";
                    break;
                default:
                    text = AcceptsBets(out _) ? "Встань на коврик: E — ставка" : "Закрыто";
                    break;
            }
            if (statusLabel.text != text) statusLabel.text = text;
        }

        private void RefreshBulbs()
        {
            if (bulbs == null || bulbs.Length == 0) return;
            _block ??= new MaterialPropertyBlock();
            float t = Time.time;
            var outcome = Outcome;
            for (int i = 0; i < bulbs.Length; i++)
            {
                if (bulbs[i] == null) continue;
                float on;
                Color color = new(1f, 0.85f, 0.45f);
                switch (State.Value)
                {
                    case RouletteState.Betting: on = (Mathf.FloorToInt(t * 3f) + i) % 2 == 0 ? 1f : 0.25f; break;
                    case RouletteState.Spinning: on = (Mathf.FloorToInt(t * 14f) - i) % 4 == 0 ? 1f : 0.2f; break;
                    case RouletteState.Result:
                        on = Mathf.FloorToInt(t * 6f) % 2 == 0 ? 1f : 0.3f;
                        if (outcome == RouletteKind.Dog) color = new Color(1f, 0.25f, 0.2f);
                        break;
                    default: on = 0.55f + 0.15f * Mathf.Sin(t * 1.5f + i); break;
                }
                bulbs[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, Color.Lerp(new Color(0.35f, 0.3f, 0.25f), color, on));
                _block.SetColor(EmissionId, color * (on * 2.2f));
                bulbs[i].SetPropertyBlock(_block);
            }
        }

        // ───────────────────────── Для HUD ─────────────────────────

        /// <summary>Строка состояния для панели в HUD.</summary>
        public string StatusText
        {
            get
            {
                switch (State.Value)
                {
                    case RouletteState.Betting: return $"Ставки принимаются ещё {Mathf.CeilToInt(TimeLeft)} с";
                    case RouletteState.Spinning: return "Колесо крутится…";
                    case RouletteState.Result:
                        var outcome = Outcome ?? RouletteKind.Orange;
                        return outcome == RouletteKind.Dog ? "Выпал пёс — все ставки сгорели!" : $"Выпало: {NameOf(outcome)} ×{Multiplier(outcome)}";
                    default:
                        return AcceptsBets(out string reason) ? "Встань на коврик перед колесом и поставь рыбу" : reason;
                }
            }
        }

        /// <summary>Ставки игрока одной строкой («Рыжие 3 · Золотая рыбка 1») или пусто.</summary>
        public string BetsText(ulong client)
        {
            _sb.Clear();
            for (int k = 0; k < 3; k++)
            {
                int amount = BetOf(client, (RouletteKind)k);
                if (amount <= 0) continue;
                if (_sb.Length > 0) _sb.Append("  ·  ");
                _sb.Append(NameOf((RouletteKind)k)).Append(' ').Append(amount);
            }
            return _sb.ToString();
        }
    }
}
