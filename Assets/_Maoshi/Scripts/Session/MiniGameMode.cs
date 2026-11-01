using System;
using System.Collections.Generic;
using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.Session
{
    /// <summary>Очки игрока в мини-игре (для игр на очки).</summary>
    public struct PlayerScore : INetworkSerializable, IEquatable<PlayerScore>
    {
        public ulong ClientId;
        public int Score;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Score);
        }

        public bool Equals(PlayerScore other) => ClientId == other.ClientId && Score == other.Score;
    }

    /// <summary>
    /// Основа любой мини-игры. Кладётся в сцену мини-игры (объект с NetworkObject).
    /// Берёт на себя общее: ожидание загрузки, отсчёт, таймер, выбывание, очки, места и отправку итогов в SessionManager.
    /// Конкретная игра наследуется и переопределяет только свои правила (OnGameTick и т.п.).
    /// Все решения принимает сервер; клиенты видят фазу, таймер, список оставшихся и очки.
    /// </summary>
    public abstract class MiniGameMode : NetworkBehaviour, IGameMode
    {
        /// <summary>Мини-игра текущей сцены (для UI). null в хабе.</summary>
        public static MiniGameMode Current { get; private set; }

        [Header("Мини-игра")]
        [Tooltip("Должен совпадать с ID в MiniGameDefinition.")]
        [SerializeField] private string miniGameId = "";
        [SerializeField] private float countdownSeconds = 3f;
        [Tooltip("0 — без лимита времени.")]
        [SerializeField] private float timeLimitSeconds = 90f;
        [Tooltip("Закончить, когда в игре остался один (или ноль при тесте в одиночку).")]
        [SerializeField] private bool endWhenOnePlayerLeft = true;
        [Tooltip("Пауза между финишем и экраном итогов.")]
        [SerializeField] private float finishDelaySeconds = 2f;

        public readonly NetworkVariable<MiniGamePhase> PhaseState = new(MiniGamePhase.WaitingForPlayers);
        public readonly NetworkVariable<double> PhaseEndTime = new();
        public NetworkList<ulong> AlivePlayers;
        public NetworkList<PlayerScore> Scores;

        // Только на сервере.
        protected readonly List<ulong> Participants = new();
        /// <summary>Группы выбывших по порядку. Выбывшие в одной группе делят место.</summary>
        protected readonly List<List<ulong>> EliminationGroups = new();
        private double _playingStartTime;
        private double _reportAt;
        private bool _reported;

        public string MiniGameId => miniGameId;
        public MiniGamePhase Phase => PhaseState.Value;
        public int AliveCount => AlivePlayers.Count;
        public float TimeLeft => PhaseEndTime.Value <= 0 ? 0f : Mathf.Max(0f, (float)(PhaseEndTime.Value - Now));
        public bool IsAlive(ulong clientId) => AlivePlayers.Contains(clientId);

        /// <summary>Подсказка под таймером (у каждой игры своя). Читается на клиентах.</summary>
        public virtual string StatusText => "";

        /// <summary>Показывать ли таблицу очков в HUD.</summary>
        public bool HasScores => Scores.Count > 0;

        protected double Now => NetworkManager.ServerTime.Time;

        private double _countdownStart;
        private bool _spawnRecheckDone;

        /// <summary>
        /// Первые 1.5 с игры кот не может выбыть «за пределы»: если он там, значит появился неудачно (задержка сети) —
        /// возвращаем его на точку появления. Возвращает true, если кот спасён и выбывание надо пропустить.
        /// </summary>
        protected bool RescueDuringStartGrace(CatController cat)
        {
            if (cat == null || Phase != MiniGamePhase.Playing || ElapsedPlaying > 1.5) return false;
            // Пока телепорт идёт по сети, сервер ещё видит кота внизу — не шлём команду каждый кадр.
            if (!_lastRescue.TryGetValue(cat.OwnerClientId, out double last) || Now - last > 0.5)
            {
                _lastRescue[cat.OwnerClientId] = Now;
                var spawner = Maoshi.Gameplay.CatSpawner.Current;
                if (spawner != null) spawner.ReturnToSpawn(cat);
            }
            return true;
        }

        private readonly Dictionary<ulong, double> _lastRescue = new();

        /// <summary>Длительность игры из настроек сцены (0 — без таймера).</summary>
        protected float TimeLimit => timeLimitSeconds;

        /// <summary>Сколько секунд идёт игра (с конца отсчёта).</summary>
        protected double ElapsedPlaying => Phase == MiniGamePhase.Playing ? Now - _playingStartTime : 0;

        /// <summary>Использует ли игра очки. Если да — места считаются по очкам.</summary>
        protected virtual bool UsesScores => false;

        protected virtual void Awake()
        {
            // NetworkList создаются до спавна объекта.
            AlivePlayers = new NetworkList<ulong>();
            Scores = new NetworkList<PlayerScore>();
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            PhaseState.OnValueChanged += OnPhaseChanged;
            ApplyControlLock(PhaseState.Value);

            if (!IsServer) return;
            NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            PhaseState.OnValueChanged -= OnPhaseChanged;
            if (Current == this) Current = null;
            CatController.ControlsLocked = false;

            if (NetworkManager == null) return;
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public override void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
                CatController.ControlsLocked = false;
            }

            base.OnDestroy();
        }

        // ───────────────────────── Цикл на сервере ─────────────────────────

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name) return;

            Participants.Clear();
            Participants.AddRange(clientsCompleted);
            Participants.Sort();

            AlivePlayers.Clear();
            Scores.Clear();
            foreach (var clientId in Participants)
            {
                AlivePlayers.Add(clientId);
                if (UsesScores) Scores.Add(new PlayerScore { ClientId = clientId, Score = 0 });
            }

            OnPlayersReady();

            _countdownStart = Now;
            _spawnRecheckDone = false;
            PhaseEndTime.Value = Now + countdownSeconds;
            PhaseState.Value = MiniGamePhase.Countdown;
        }

        protected virtual void Update()
        {
            if (!IsSpawned || !IsServer) return;

            switch (PhaseState.Value)
            {
                case MiniGamePhase.Countdown:
                    // Через полсекунды отсчёта ещё раз ставим всех котов на их точки: у игроков по сети кот мог
                    // появиться не там (вне площадки, в мебели) — и сразу проиграть.
                    if (!_spawnRecheckDone && Now >= _countdownStart + 0.6)
                    {
                        _spawnRecheckDone = true;
                        if (Maoshi.Gameplay.CatSpawner.Current != null) Maoshi.Gameplay.CatSpawner.Current.ReturnAllToSpawn();
                    }
                    if (Now >= PhaseEndTime.Value)
                    {
                        _playingStartTime = Now;
                        PhaseEndTime.Value = timeLimitSeconds > 0f ? Now + timeLimitSeconds : 0;
                        PhaseState.Value = MiniGamePhase.Playing;
                        OnGameStarted();
                    }
                    break;

                case MiniGamePhase.Playing:
                    OnGameTick();
                    if (PhaseState.Value == MiniGamePhase.Playing && timeLimitSeconds > 0f && Now >= PhaseEndTime.Value)
                        FinishGame();
                    break;

                case MiniGamePhase.Finished:
                    if (!_reported && Now >= _reportAt)
                    {
                        _reported = true;
                        ReportResults();
                    }
                    break;
            }
        }

        /// <summary>Игрок выбыл. Вызывать только на сервере.</summary>
        protected void Eliminate(ulong clientId) => EliminateGroup(new List<ulong> { clientId });

        /// <summary>Несколько игроков выбыли одновременно — они делят одно место. Только на сервере.</summary>
        protected void EliminateGroup(List<ulong> clientIds)
        {
            if (!IsServer || PhaseState.Value != MiniGamePhase.Playing) return;

            var group = new List<ulong>();
            foreach (var clientId in clientIds)
            {
                if (!AlivePlayers.Contains(clientId)) continue;
                AlivePlayers.Remove(clientId);
                group.Add(clientId);
            }

            if (group.Count == 0) return;
            EliminationGroups.Add(group);
            foreach (var clientId in group)
            {
                SessionStats.Add(clientId, Stat.Eliminated);
                OnPlayerEliminated(clientId);
            }

            int playersToEnd = Participants.Count > 1 ? 1 : 0;
            if (endWhenOnePlayerLeft && AlivePlayers.Count <= playersToEnd)
                FinishGame();
        }

        /// <summary>Закончить игру (по правилам или по таймеру). Только на сервере.</summary>
        /// <summary>Сервер, консоль разработчика: досрочно закончить мини-игру (итоги по текущему положению).</summary>
        public bool DebugFinish()
        {
            if (!IsServer || PhaseState.Value == MiniGamePhase.Finished) return false;
            FinishGame();
            return true;
        }

        protected void FinishGame()
        {
            if (!IsServer || PhaseState.Value == MiniGamePhase.Finished) return;

            PhaseState.Value = MiniGamePhase.Finished;
            PhaseEndTime.Value = 0;
            _reportAt = Now + finishDelaySeconds;
            OnGameFinished();
        }

        private void ReportResults()
        {
            var placements = BuildPlacements();

            if (SessionManager.Instance != null)
                SessionManager.Instance.ReportResults(miniGameId, placements);
            else
                Debug.LogWarning("[Maoshi] Нет SessionManager — итоги мини-игры некуда отправить (сцену запустили напрямую?).");
        }

        /// <summary>
        /// Места: в играх на очки — по очкам; иначе оставшиеся делят 1-е место,
        /// выбывшие — по порядку (кто дольше продержался, тот выше; выбывшие одновременно делят место).
        /// </summary>
        protected virtual List<Placement> BuildPlacements()
        {
            if (UsesScores) return PlacementsByScore();

            var result = new List<Placement>();

            foreach (var clientId in AlivePlayers)
                result.Add(new Placement(clientId, 1));

            int place = AlivePlayers.Count + 1;
            for (int i = EliminationGroups.Count - 1; i >= 0; i--)
            {
                var group = EliminationGroups[i];
                foreach (var clientId in group) result.Add(new Placement(clientId, place));
                place += group.Count;
            }

            return result;
        }

        /// <summary>Места по очкам: больше очков — выше. Равные очки делят место.</summary>
        protected List<Placement> PlacementsByScore()
        {
            var sorted = new List<PlayerScore>();
            foreach (var score in Scores) sorted.Add(score);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            var result = new List<Placement>();
            for (int i = 0; i < sorted.Count; i++)
            {
                int place = i > 0 && sorted[i].Score == sorted[i - 1].Score ? result[i - 1].Place : i + 1;
                result.Add(new Placement(sorted[i].ClientId, place));
            }

            return result;
        }

        // ───────────────────────── Очки ─────────────────────────

        /// <summary>Сервер: добавить очки игроку.</summary>
        protected void AddScore(ulong clientId, int amount)
        {
            if (!IsServer) return;

            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId != clientId) continue;
                var entry = Scores[i];
                entry.Score = Mathf.Max(0, entry.Score + amount);
                Scores[i] = entry;
                return;
            }
        }

        public int GetScore(ulong clientId)
        {
            foreach (var entry in Scores)
            {
                if (entry.ClientId == clientId) return entry.Score;
            }

            return 0;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // Ушедший игрок просто выбывает и в итоги не попадает.
            if (PhaseState.Value == MiniGamePhase.Playing) Eliminate(clientId);
            else if (AlivePlayers.Contains(clientId)) AlivePlayers.Remove(clientId);

            Participants.Remove(clientId);
            foreach (var group in EliminationGroups) group.Remove(clientId);
            EliminationGroups.RemoveAll(group => group.Count == 0);

            for (int i = Scores.Count - 1; i >= 0; i--)
            {
                if (Scores[i].ClientId == clientId) Scores.RemoveAt(i);
            }
        }

        // ───────────────────────── Клиенты ─────────────────────────

        private void OnPhaseChanged(MiniGamePhase previous, MiniGamePhase next)
        {
            ApplyControlLock(next);
            OnPhaseChangedClient(next);
        }

        private static void ApplyControlLock(MiniGamePhase phase)
        {
            // Во время отсчёта и после финиша коты стоят на месте.
            CatController.ControlsLocked = phase != MiniGamePhase.Playing;
        }

        // ───────────────────────── Точки расширения для конкретных игр ─────────────────────────

        /// <summary>Сервер: все загрузились, отсчёт вот-вот начнётся. Здесь удобно расставить объекты.</summary>
        protected virtual void OnPlayersReady() { }

        /// <summary>Сервер: отсчёт закончился, игра пошла.</summary>
        protected virtual void OnGameStarted() { }

        /// <summary>Сервер: каждый кадр во время игры. Здесь проверяются правила.</summary>
        protected virtual void OnGameTick() { }

        /// <summary>Сервер: игрок выбыл.</summary>
        protected virtual void OnPlayerEliminated(ulong clientId) { }

        /// <summary>Сервер: игра закончилась.</summary>
        protected virtual void OnGameFinished() { }

        /// <summary>Все: фаза сменилась (для звуков и эффектов).</summary>
        protected virtual void OnPhaseChangedClient(MiniGamePhase phase) { }
    }
}
