using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.MouseHoles
{
    /// <summary>
    /// «Мышиные норки»: мыши выскакивают из случайных норок на пару секунд.
    /// Первый кот, добежавший до мыши, ловит её: +1 очко, золотая мышь — +3.
    /// Никто не выбывает; по таймеру места считаются по очкам. Толкать соперников от норки — можно и нужно.
    /// Сервер решает, где мыши и сколько очков. Поймал ли — замечает сам владелец кота (его позиция точная,
    /// без сетевой задержки) и отправляет заявку; сервер проверяет её с запасом на задержку.
    /// Так клиент ловит так же честно, как хост.
    /// </summary>
    public class MouseHolesMode : MiniGameMode
    {
        [Header("Норки")]
        [SerializeField] private MouseHole[] holes;
        [SerializeField] private float catchRadius = 0.9f;
        [Tooltip("Ловить можно только стоя на земле (не в прыжке над норкой).")]
        [SerializeField] private float catchMaxHeight = 1.2f;
        [Tooltip("Запас к радиусу при проверке заявки на сервере: там позиция чужого кота чуть отстаёт.")]
        [SerializeField] private float serverTolerance = 1.5f;
        [Tooltip("Сколько секунд после того, как мышь спряталась, ещё принимаем заявку (сетевая задержка).")]
        [SerializeField] private float lateClaimGrace = 0.25f;

        [Header("Мыши")]
        [SerializeField] private float minSpawnInterval = 0.5f;
        [SerializeField] private float maxSpawnInterval = 1.1f;
        [SerializeField] private float mouseLifetime = 1.8f;
        [SerializeField, Range(0f, 1f)] private float goldenChance = 0.12f;
        [SerializeField] private int goldenPoints = 3;

        /// <summary>Активные мыши: номер норки * 2 + (1, если золотая).</summary>
        public NetworkList<int> ActiveMice;

        private double[] _expireAt;
        private int[] _lastCode;      // какая мышь была в норке последней (для поздних заявок)
        private bool[] _caught;       // её уже поймали
        private double _nextSpawnAt;
        private float _nextClaimTime; // клиент: не спамим заявками

        protected override bool UsesScores => true;
        public override string StatusText => $"Лови мышей! Золотая — {goldenPoints} очка";

        protected override void Awake()
        {
            base.Awake();
            ActiveMice = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ActiveMice.OnListChanged += OnMiceChanged;
            RefreshHoles();
        }

        public override void OnNetworkDespawn()
        {
            ActiveMice.OnListChanged -= OnMiceChanged;
            base.OnNetworkDespawn();
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnGameStarted()
        {
            _expireAt = new double[holes.Length];
            _lastCode = new int[holes.Length];
            _caught = new bool[holes.Length];
            for (int i = 0; i < holes.Length; i++) _lastCode[i] = -1;
            _nextSpawnAt = Now + 0.4;
        }

        protected override void OnGameTick()
        {
            if (holes == null || holes.Length == 0) return;

            // 1. Мыши, которых не успели поймать, прячутся.
            for (int i = ActiveMice.Count - 1; i >= 0; i--)
            {
                int hole = ActiveMice[i] >> 1;
                if (Now >= _expireAt[hole]) ActiveMice.RemoveAt(i);
            }

            // 2. Ловля — по заявкам владельцев котов (CatchRpc).

            // 3. Новые мыши. Одновременно — не больше, чем игроков + 1, чтобы всем доставалось.
            int maxActive = Mathf.Clamp(Participants.Count + 1, 2, holes.Length - 1);
            if (Now >= _nextSpawnAt && ActiveMice.Count < maxActive)
            {
                int hole = PickFreeHole();
                if (hole >= 0)
                {
                    bool golden = Random.value < goldenChance;
                    int code = hole * 2 + (golden ? 1 : 0);
                    ActiveMice.Add(code);
                    _lastCode[hole] = code;
                    _caught[hole] = false;
                    // Золотая убегает быстрее.
                    _expireAt[hole] = Now + mouseLifetime * (golden ? 0.7f : 1f);
                }

                _nextSpawnAt = Now + Random.Range(minSpawnInterval, maxSpawnInterval);
            }
        }

        protected override void OnGameFinished()
        {
            ActiveMice.Clear();
        }

        /// <summary>Заявка владельца кота: «я у норки, где мышь». Сервер проверяет и начисляет очки.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CatchRpc(int code, RpcParams rpcParams = default)
        {
            if (Phase != MiniGamePhase.Playing || _expireAt == null) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsAlive(sender)) return;

            int hole = code >> 1;
            if (hole < 0 || hole >= holes.Length) return;

            // Мышь ещё сидит — или только что спряталась (заявка шла по сети).
            bool active = ActiveMice.Contains(code);
            bool late = !active && _lastCode[hole] == code && !_caught[hole] && Now <= _expireAt[hole] + lateClaimGrace;
            if (!active && !late) return;

            // Грубая защита: на сервере кот должен быть хотя бы примерно рядом.
            var cat = CatController.FindByOwner(sender);
            if (cat == null) return;
            Vector3 offset = cat.transform.position - holes[hole].transform.position;
            offset.y = 0f;
            float limit = catchRadius + serverTolerance;
            if (offset.sqrMagnitude > limit * limit) return;

            _caught[hole] = true;
            if (active) ActiveMice.Remove(code);
            AddScore(sender, (code & 1) == 1 ? goldenPoints : 1);
        }

        private int PickFreeHole()
        {
            // Несколько попыток найти норку без мыши.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int hole = Random.Range(0, holes.Length);
                if (!IsHoleBusy(hole)) return hole;
            }

            return -1;
        }

        private bool IsHoleBusy(int hole)
        {
            foreach (int code in ActiveMice)
            {
                if (code >> 1 == hole) return true;
            }

            return false;
        }

        // ───────────────────────── Клиенты ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned || Phase != MiniGamePhase.Playing || holes == null) return;
            if (Time.time < _nextClaimTime) return;

            var cat = CatController.FindByOwner(NetworkManager.LocalClientId);
            if (cat == null || !IsAlive(NetworkManager.LocalClientId)) return;

            Vector3 catPosition = cat.transform.position;
            foreach (int code in ActiveMice)
            {
                int hole = code >> 1;
                if (hole < 0 || hole >= holes.Length || holes[hole] == null) continue;

                Vector3 offset = catPosition - holes[hole].transform.position;
                if (offset.y > catchMaxHeight) continue;
                offset.y = 0f;
                if (offset.sqrMagnitude > catchRadius * catchRadius) continue;

                CatchRpc(code);
                _nextClaimTime = Time.time + 0.15f; // ответ сервера придёт через список мышей
                return;
            }
        }

        private void OnMiceChanged(NetworkListEvent<int> change) => RefreshHoles();

        private void RefreshHoles()
        {
            if (holes == null) return;

            for (int i = 0; i < holes.Length; i++)
            {
                bool visible = false;
                bool golden = false;
                foreach (int code in ActiveMice)
                {
                    if (code >> 1 != i) continue;
                    visible = true;
                    golden = (code & 1) == 1;
                    break;
                }

                if (holes[i] != null) holes[i].SetMouse(visible, golden);
            }
        }
    }
}
