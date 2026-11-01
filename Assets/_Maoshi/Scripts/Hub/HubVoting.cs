using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Maoshi.Hub
{
    /// <summary>Фаза голосования во дворе.</summary>
    public enum VotePhase : byte
    {
        /// <summary>Двор загружается у игроков.</summary>
        Waiting,
        /// <summary>Идёт голосование: коты встают на круги.</summary>
        Voting,
        /// <summary>Победитель выбран, через пару секунд загрузка мини-игры.</summary>
        Announcing
    }

    /// <summary>
    /// Голосование за следующую мини-игру прямо во дворе: игрок встаёт котом на круг с игрой.
    /// Варианты: три случайные игры (без только что сыгранных) и «Кот в мешке» — случайная игра, которую узнают в конце.
    /// Сервер считает котов на кругах; при ничьей победителя выбирает «барабан» — подсветка бегает по лидерам и
    /// замедляется (анимация у всех одинаковая: по сетевому времени и победителю). Пока крутится кошачья рулетка,
    /// загрузка мини-игры ждёт её результата. Клиенты только показывают состояние.
    /// </summary>
    public class HubVoting : NetworkBehaviour
    {
        /// <summary>Голосование текущей сцены (для UI). null вне двора.</summary>
        public static HubVoting Current { get; private set; }

        [SerializeField] private VotePad[] pads;
        [SerializeField] private float voteDuration = 45f;
        [Tooltip("Если все уже проголосовали, до конца остаётся не больше стольких секунд.")]
        [SerializeField] private float allVotedRemaining = 5f;
        [Tooltip("Сколько показываем победителя после «барабана», до загрузки игры.")]
        [SerializeField] private float announceDuration = 2.5f;
        [Tooltip("Сколько последних сыгранных игр не предлагать (если есть из чего выбрать).")]
        [SerializeField] private int avoidRecent = 3;

        public readonly NetworkVariable<VotePhase> Phase = new(VotePhase.Waiting);
        public readonly NetworkVariable<double> PhaseEndTime = new();
        public readonly NetworkVariable<int> WinnerIndex = new(-1);
        /// <summary>Какая игра на самом деле запустится (для «Кота в мешке» — выпавшая).</summary>
        public readonly NetworkVariable<FixedString64Bytes> ResolvedId = new();
        public readonly NetworkVariable<double> RevealStart = new();
        public NetworkList<FixedString64Bytes> OptionIds;
        public NetworkList<int> VoteCounts;

        private bool _startRequested;
        private bool _sceneReady;
        private readonly List<int> _candidates = new();

        private double Now => NetworkManager.ServerTime.Time;

        /// <summary>Сколько секунд осталось до конца фазы.</summary>
        public float TimeLeft => Mathf.Max(0f, (float)(PhaseEndTime.Value - Now));

        /// <summary>Полная длительность голосования (для полоски времени).</summary>
        public float VoteDuration => voteDuration;

        public IReadOnlyList<VotePad> Pads => pads;

        /// <summary>Название игры, которая запустится (или пусто).</summary>
        public string WinnerTitle
        {
            get
            {
                string id = ResolvedId.Value.ToString();
                if (string.IsNullOrEmpty(id) && WinnerIndex.Value >= 0 && WinnerIndex.Value < OptionIds.Count) id = OptionIds[WinnerIndex.Value].ToString();
                return string.IsNullOrEmpty(id) ? "" : TitleOf(id);
            }
        }

        /// <summary>Победил ли «Кот в мешке».</summary>
        public bool WinnerIsRandom => WinnerIndex.Value >= 0 && WinnerIndex.Value < OptionIds.Count && OptionIds[WinnerIndex.Value].ToString() == MiniGameCards.RandomId;

        private void Awake()
        {
            OptionIds = new NetworkList<FixedString64Bytes>();
            VoteCounts = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            OptionIds.OnListChanged += OnListChanged;
            VoteCounts.OnListChanged += OnCountsChanged;
            WinnerIndex.OnValueChanged += OnWinnerChanged;
            RefreshPads();

            if (IsServer)
                NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        public override void OnNetworkDespawn()
        {
            OptionIds.OnListChanged -= OnListChanged;
            VoteCounts.OnListChanged -= OnCountsChanged;
            WinnerIndex.OnValueChanged -= OnWinnerChanged;
            if (Current == this) Current = null;

            if (NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        public override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        // ───────────────────────── Сервер ─────────────────────────

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            // Двор загрузился у всех. Голосование начнётся, когда сессия перейдёт в фазу Hub
            // (сразу при первом заходе или после аукциона за зону).
            if (sceneName == gameObject.scene.name) _sceneReady = true;
        }

        private void StartVoting()
        {
            var session = SessionManager.Instance;
            if (session == null || session.Catalog == null)
            {
                Debug.LogWarning("[Maoshi] Голосование: нет SessionManager или каталога.");
                return;
            }

            var options = PickOptions(session, NetworkPlayer.All.Count);
            OptionIds.Clear();
            VoteCounts.Clear();
            foreach (var id in options)
            {
                OptionIds.Add(new FixedString64Bytes(id));
                VoteCounts.Add(0);
            }

            WinnerIndex.Value = -1;
            ResolvedId.Value = default;
            PhaseEndTime.Value = Now + voteDuration;
            Phase.Value = VotePhase.Voting;
        }

        /// <summary>Игры, подходящие по числу игроков (в одиночку в редакторе — все).</summary>
        private static List<MiniGameDefinition> Fitting(SessionManager session, int playerCount)
        {
            var all = new List<MiniGameDefinition>();
            foreach (var definition in session.Catalog.MiniGames)
            {
                if (definition != null) all.Add(definition);
            }

            var fitting = all.FindAll(d => d.MinPlayers <= playerCount);
            return fitting.Count > 0 ? fitting : all;
        }

        /// <summary>
        /// Варианты: на все круги, кроме последнего, — случайные игры (сначала давно не игранные),
        /// на последний — «Кот в мешке», если в запасе остались игры, которых нет среди вариантов.
        /// </summary>
        private List<string> PickOptions(SessionManager session, int playerCount)
        {
            var fitting = Fitting(session, playerCount);
            int padCount = pads != null ? pads.Length : 3;
            int gameSlots = Mathf.Max(1, padCount - 1);

            // Перемешиваем (Фишер — Йетс), потом недавно сыгранные уходят в конец списка.
            for (int i = fitting.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (fitting[i], fitting[j]) = (fitting[j], fitting[i]);
            }

            var history = session.PlayedHistory;
            int Recency(MiniGameDefinition d)
            {
                // 0 — давно не играли; чем свежее, тем больше.
                for (int k = history.Count - 1, age = 0; k >= 0 && age < avoidRecent; k--, age++)
                    if (history[k] == d.Id) return avoidRecent - age;
                return 0;
            }
            var ordered = new List<MiniGameDefinition>(fitting);
            ordered.Sort((a, b) => Recency(a).CompareTo(Recency(b))); // List.Sort нестабилен, но порядок внутри групп и так случайный
            // Только что сыгранную не предлагаем никогда, если есть хоть одна другая.
            if (ordered.Count > 1) ordered.RemoveAll(d => d.Id == session.LastMiniGameId);

            var result = new List<string>();
            for (int i = 0; i < ordered.Count && result.Count < gameSlots; i++) result.Add(ordered[i].Id);

            // «Кот в мешке» — только если есть что выбрать сверх показанного.
            if (ordered.Count > result.Count && padCount > result.Count) result.Add(MiniGameCards.RandomId);
            return result;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (Phase.Value == VotePhase.Waiting && _sceneReady)
            {
                var session = SessionManager.Instance;
                if (session != null && session.Phase.Value == SessionPhase.Hub) StartVoting();
            }

            if (Application.isEditor) DebugHotkeys();

            if (Phase.Value == VotePhase.Voting)
            {
                // Пока идёт «Кошачья разборка», голосование стоит на паузе.
                var duel = Showdown.ShowdownManager.Instance;
                if (duel != null && duel.IsActive)
                {
                    PhaseEndTime.Value += Time.deltaTime;
                    return;
                }

                int totalVotes = CountVotes();

                // Все проголосовали — не ждём весь таймер.
                int players = NetworkPlayer.All.Count;
                if (players > 0 && totalVotes >= players && PhaseEndTime.Value - Now > allVotedRemaining)
                    PhaseEndTime.Value = Now + allVotedRemaining;

                if (Now >= PhaseEndTime.Value) Announce();
            }
            else if (Phase.Value == VotePhase.Announcing && !_startRequested && Now >= PhaseEndTime.Value)
            {
                // Рулетка ещё крутится — дожидаемся выплат, а потом уже грузим игру.
                if (CatRoulette.Current != null && CatRoulette.Current.HoldsHub) return;

                _startRequested = true;
                var session = SessionManager.Instance;
                string id = ResolvedId.Value.ToString();
                if (session != null && !string.IsNullOrEmpty(id)) session.StartMiniGame(id);
            }
        }

        /// <summary>
        /// Только в редакторе: хост нажимает 1–9, 0, «-», «=» (или Shift+цифра для 11-й и дальше) во дворе — сразу запускается мини-игра с этим номером в каталоге.
        /// Чтобы быстро тестировать конкретную игру без голосования.
        /// </summary>
        private void DebugHotkeys()
        {
            var keyboard = Maoshi.GameKeyboard.Current;
            var session = SessionManager.Instance;
            if (keyboard == null || session == null || session.Catalog == null || _startRequested) return;
            if (Showdown.ShowdownManager.Instance != null && Showdown.ShowdownManager.Instance.IsActive) return;

            var games = session.Catalog.MiniGames;
            // 1–9, 0, «-», «=» — игры 1–12; Shift + 1…9 — игры 11–19.
            bool shift = keyboard.shiftKey.isPressed;
            Key[] extra = { Key.Digit0, Key.Minus, Key.Equals };
            for (int i = 0; i < games.Count && i < 19; i++)
            {
                Key code;
                if (shift) { if (i < 10) continue; code = Key.Digit1 + (i - 10); }
                else { if (i >= 12) continue; code = i < 9 ? Key.Digit1 + i : extra[i - 9]; }
                var key = keyboard[code];
                if (key == null || !key.wasPressedThisFrame || games[i] == null) continue;

                _startRequested = true;
                session.StartMiniGame(games[i].Id);
                return;
            }
        }

        /// <summary>Считает котов на кругах и обновляет счётчики. Возвращает число голосов.</summary>
        private int CountVotes()
        {
            int total = 0;

            for (int option = 0; option < OptionIds.Count && option < pads.Length; option++)
            {
                int votes = 0;
                foreach (var player in NetworkPlayer.All)
                {
                    var cat = CatController.FindByOwner(player.OwnerClientId);
                    if (cat != null && pads[option].Contains(cat.transform.position)) votes++;
                }

                total += votes;
                if (VoteCounts[option] != votes) VoteCounts[option] = votes;
            }

            return total;
        }

        private void Announce()
        {
            if (OptionIds.Count == 0) return;

            // Больше всех голосов; при ничьей (или если никто не голосовал) — «барабан» среди лидеров.
            Leaders(_candidates);
            int winner = _candidates[Random.Range(0, _candidates.Count)];

            string id = OptionIds[winner].ToString();
            if (id == MiniGameCards.RandomId)
            {
                // Кот в мешке: любая подходящая игра, кроме только что сыгранной (может совпасть и с соседним кругом).
                var session = SessionManager.Instance;
                var pool = Fitting(session, NetworkPlayer.All.Count);
                if (pool.Count > 1) pool.RemoveAll(d => d.Id == session.LastMiniGameId);
                id = pool[Random.Range(0, pool.Count)].Id;
            }

            ResolvedId.Value = new FixedString64Bytes(id);
            WinnerIndex.Value = winner;
            RevealStart.Value = Now;
            PhaseEndTime.Value = Now + RevealSeconds(_candidates.Count) + announceDuration;
            Phase.Value = VotePhase.Announcing;

            // Ставки в рулетке закрываются: если кто-то поставил — колесо крутится сейчас, игра подождёт.
            if (CatRoulette.Current != null) CatRoulette.Current.CloseBetting();
        }

        /// <summary>Индексы вариантов с наибольшим числом голосов (никто не голосовал — все варианты).</summary>
        private void Leaders(List<int> into)
        {
            into.Clear();
            int best = -1;
            for (int i = 0; i < VoteCounts.Count; i++)
            {
                if (VoteCounts[i] > best)
                {
                    best = VoteCounts[i];
                    into.Clear();
                }

                if (VoteCounts[i] == best) into.Add(i);
            }
        }

        // ───────────────────────── «Барабан» выбора ─────────────────────────

        private const int RevealLaps = 3;

        /// <summary>Сколько секунд бегает подсветка при n претендентах (один — сразу).</summary>
        public static float RevealSeconds(int candidates)
        {
            if (candidates <= 1) return 0.6f;
            int steps = candidates * RevealLaps + 1;
            float total = 0f;
            for (int k = 0; k < steps; k++) total += StepSeconds(k, steps);
            return total;
        }

        private static float StepSeconds(int k, int steps)
        {
            float x = (float)k / steps;
            return 0.07f + 0.32f * x * x * x;
        }

        /// <summary>
        /// Какой вариант сейчас подсвечен «барабаном» и закончил ли он крутиться.
        /// Считается у всех одинаково: претенденты — из итоговых голосов, конец — на победителе.
        /// </summary>
        public int RevealHighlight(out bool settled)
        {
            settled = true;
            if (Phase.Value != VotePhase.Announcing || WinnerIndex.Value < 0) return WinnerIndex.Value;

            Leaders(_candidates);
            int n = _candidates.Count;
            int winnerSlot = _candidates.IndexOf(WinnerIndex.Value);
            if (n <= 1 || winnerSlot < 0)
            {
                settled = Now - RevealStart.Value >= 0.6f;
                return WinnerIndex.Value;
            }

            int steps = n * RevealLaps + 1;
            int startSlot = ((winnerSlot - (steps - 1)) % n + n) % n;
            float elapsed = (float)(Now - RevealStart.Value);
            float t = 0f;
            for (int k = 0; k < steps; k++)
            {
                t += StepSeconds(k, steps);
                if (elapsed < t)
                {
                    settled = false;
                    return _candidates[(startSlot + k) % n];
                }
            }
            return WinnerIndex.Value;
        }

        /// <summary>Клиент: кто из игроков стоит на круге варианта (по позициям котов, только для показа).</summary>
        public void VotersOf(int option, List<NetworkPlayer> into)
        {
            into.Clear();
            if (pads == null || option < 0 || option >= pads.Length || pads[option] == null) return;
            foreach (var player in NetworkPlayer.All)
            {
                var cat = CatController.FindByOwner(player.OwnerClientId);
                if (cat != null && pads[option].Contains(cat.transform.position)) into.Add(player);
            }
        }

        /// <summary>Клиент: на каком круге стоит кот игрока (-1 — ни на каком).</summary>
        public int PadOf(ulong clientId)
        {
            var cat = CatController.FindByOwner(clientId);
            if (cat == null || pads == null) return -1;
            for (int i = 0; i < pads.Length && i < OptionIds.Count; i++)
                if (pads[i] != null && pads[i].Contains(cat.transform.position)) return i;
            return -1;
        }

        // ───────────────────────── Клиенты ─────────────────────────

        private void OnListChanged(NetworkListEvent<FixedString64Bytes> change) => RefreshPads();
        private void OnCountsChanged(NetworkListEvent<int> change) => RefreshPads();
        private void OnWinnerChanged(int previous, int next) => RefreshPads();

        private int _shownHighlight = -2;
        private bool _shownSettled;

        private float _nextVotersRefresh;

        private void LateUpdate()
        {
            if (!IsSpawned) return;
            // Кто стоит на кругах — по позициям котов, обновляем афиши несколько раз в секунду.
            if (Phase.Value == VotePhase.Voting && Time.time >= _nextVotersRefresh)
            {
                _nextVotersRefresh = Time.time + 0.25f;
                RefreshPads();
            }
            if (Phase.Value != VotePhase.Announcing) return;
            int highlight = RevealHighlight(out bool settled);
            if (highlight == _shownHighlight && settled == _shownSettled) return;
            _shownHighlight = highlight;
            _shownSettled = settled;
            RefreshPads();
        }

        private void RefreshPads()
        {
            if (pads == null) return;

            bool announcing = IsSpawned && Phase.Value == VotePhase.Announcing;
            bool settled = true;
            int highlight = announcing ? RevealHighlight(out settled) : -1;

            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i] == null) continue;

                if (i < OptionIds.Count)
                {
                    string id = OptionIds[i].ToString();
                    bool random = id == MiniGameCards.RandomId;
                    int votes = i < VoteCounts.Count ? VoteCounts[i] : 0;
                    string title = random ? "Кот в мешке" : TitleOf(id);
                    if (random && announcing && settled && i == WinnerIndex.Value) title = "Мешок: " + WinnerTitle;
                    var state = !announcing ? VotePad.State.Idle
                        : i == highlight ? (settled ? VotePad.State.Winner : VotePad.State.Lit)
                        : VotePad.State.Dim;
                    Color padColor = MiniGameCards.PadColor(i, random);
                    pads[i].Show(i + 1, title, votes, padColor, state);

                    var definition = random ? null : DefinitionOf(id);
                    var card = MiniGameCards.Get(id, definition != null ? definition.Description : "");
                    pads[i].ShowPoster(i + 1, card.Genre, card.GenreColor, title, card.Tagline, VotersLine(i), padColor);
                }
                else
                {
                    pads[i].Hide();
                }
            }
        }

        private readonly List<NetworkPlayer> _votersBuffer = new();
        private readonly System.Text.StringBuilder _votersText = new();

        /// <summary>Строка «кто за эту игру» с цветами игроков (rich text TextMesh).</summary>
        private string VotersLine(int option)
        {
            VotersOf(option, _votersBuffer);
            if (_votersBuffer.Count == 0) return "<color=#5a4a58>встань на круг, чтобы голосовать</color>";
            _votersText.Clear();
            foreach (var player in _votersBuffer)
            {
                if (_votersText.Length > 0) _votersText.Append("  ");
                _votersText.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(player.Color)).Append(">●</color> ").Append(player.DisplayName);
            }
            return _votersText.ToString();
        }

        public static string TitleOf(string id)
        {
            if (id == MiniGameCards.RandomId) return "Кот в мешке";
            var session = SessionManager.Instance;
            var definition = session != null && session.Catalog != null ? session.Catalog.Find(id) : null;
            return definition != null ? definition.DisplayName : id;
        }

        public static MiniGameDefinition DefinitionOf(string id)
        {
            var session = SessionManager.Instance;
            return session != null && session.Catalog != null ? session.Catalog.Find(id) : null;
        }
    }
}
