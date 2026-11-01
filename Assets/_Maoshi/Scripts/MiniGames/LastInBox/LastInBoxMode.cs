using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.LastInBox
{
    /// <summary>
    /// «Последний в коробке» — коробка как зона спасения, игра идёт раундами:
    /// 1) коробка стоит на месте, идёт отсчёт — все бегут к ней и толкаются за место;
    /// 2) время вышло — кто не внутри, тот выбыл (выбывшие на одной проверке делят место);
    /// 3) коробка исчезает, через паузу «выпрыгивает» в другом месте, меньше, а раунд — короче.
    /// Выбывшие уезжают на островок, откуда не допрыгнуть обратно.
    /// «Внутри» засчитывается, если так считает сервер ИЛИ владелец кота (его позиция без сетевой задержки).
    /// </summary>
    public class LastInBoxMode : MiniGameMode
    {
        [Header("Коробка")]
        [SerializeField] private BoxVisual box;
        [SerializeField] private float baseSize = 2.2f;
        [SerializeField] private float sizePerPlayer = 0.75f;
        [SerializeField] private float shrinkFactor = 0.8f;
        [SerializeField] private float minSize = 1.1f;
        [Tooltip("Отступ от стенок: кот, застрявший в стенке наполовину, считается снаружи.")]
        [SerializeField] private float insideMargin = 0.1f;
        [Tooltip("Выше этого — не внутри (кот запрыгнул на что-то высокое). Прыжок внутри коробки не наказывается.")]
        [SerializeField] private float insideMaxHeight = 3f;

        [Header("Раунды")]
        [SerializeField] private float firstRoundTime = 10f;
        [SerializeField] private float roundTime = 7f;
        [SerializeField] private float roundTimeStep = 0.5f;
        [SerializeField] private float minRoundTime = 4.5f;
        [Tooltip("Сколько секунд коробки нет после проверки.")]
        [SerializeField] private float hiddenTime = 1.5f;

        [Header("Новое место коробки")]
        [Tooltip("Коробка не появляется дальше этого от центра арены.")]
        [SerializeField] private float arenaHalfSize = 10f;
        [SerializeField] private float minMoveDistance = 4f;
        [SerializeField] private float maxMoveDistance = 8f;

        [Tooltip("Писать в консоль, кто где был на каждой проверке (для отладки).")]
        [SerializeField] private bool logChecks = true;

        [Header("Скамейка выбывших")]
        [SerializeField] private Transform bench;
        [SerializeField] private float benchSpacing = 1.5f;

        public readonly NetworkVariable<float> BoxSize = new(4f);
        public readonly NetworkVariable<Vector3> BoxCenter = new();
        public readonly NetworkVariable<bool> BoxVisible = new(true);
        /// <summary>Когда проверка (пока коробка видна) или когда коробка появится (пока спрятана).</summary>
        public readonly NetworkVariable<double> NextEventTime = new();
        public readonly NetworkVariable<int> Round = new();

        private int _benchIndex;
        private float _currentRoundTime;

        // Сервер: что сообщили сами владельцы котов («я в коробке») и когда.
        private readonly Dictionary<ulong, double> _insideReportedAt = new();
        // Клиент: последнее отправленное состояние.
        private bool _reportedInside;
        private float _nextReportTime;

        public override string StatusText
        {
            get
            {
                if (NextEventTime.Value <= 0) return "Залезай в коробку!";
                int left = Mathf.Max(0, Mathf.CeilToInt((float)(NextEventTime.Value - Now)));
                if (!BoxVisible.Value) return "Коробка исчезла… где она появится?";
                return left > 0 ? $"Раунд {Round.Value}: {left} c — все в коробку!" : "Проверка!";
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BoxSize.OnValueChanged += OnSizeChanged;
            BoxCenter.OnValueChanged += OnCenterChanged;
            BoxVisible.OnValueChanged += OnVisibleChanged;
            if (IsServer && box != null) BoxCenter.Value = box.transform.position;

            if (box != null)
            {
                box.SetSize(BoxSize.Value);
                box.SetPosition(BoxCenter.Value);
                box.SetVisible(BoxVisible.Value, false);
            }
        }

        public override void OnNetworkDespawn()
        {
            BoxSize.OnValueChanged -= OnSizeChanged;
            BoxCenter.OnValueChanged -= OnCenterChanged;
            BoxVisible.OnValueChanged -= OnVisibleChanged;
            base.OnNetworkDespawn();
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            // Первый раунд: коробка просторная, места хватает всем.
            BoxSize.Value = baseSize + sizePerPlayer * Participants.Count;
            BoxVisible.Value = true;
            NextEventTime.Value = 0;
            _currentRoundTime = roundTime;
        }

        protected override void OnGameStarted()
        {
            Round.Value = 1;
            NextEventTime.Value = Now + firstRoundTime;
        }

        protected override void OnGameTick()
        {
            if (NextEventTime.Value <= 0 || Now < NextEventTime.Value) return;

            if (BoxVisible.Value) CheckRound();
            else ShowNextBox();
        }

        /// <summary>Время раунда вышло: кто снаружи — выбыл; коробка исчезает.</summary>
        private void CheckRound()
        {
            var outside = new List<ulong>();
            foreach (var clientId in AlivePlayers)
            {
                var cat = CatController.FindByOwner(clientId);
                bool serverInside = cat != null && IsInside(cat.transform.position);
                // Владелец видит своего кота без задержки — его отчёт свежий, если пришёл за последние полсекунды.
                bool ownerInside = _insideReportedAt.TryGetValue(clientId, out double at) && Now - at < 0.5;
                bool inside = serverInside || ownerInside;
                if (!inside) outside.Add(clientId);

                if (logChecks)
                    Debug.Log($"[Maoshi] Коробка, раунд {Round.Value}: игрок {clientId}, кот {(cat != null ? cat.transform.position.ToString("F2") : "не найден")}, " +
                              $"коробка {BoxCenter.Value:F2} размер {BoxSize.Value:F2}; сервер: {(serverInside ? "внутри" : "снаружи")}, " +
                              $"владелец: {(ownerInside ? "внутри" : "снаружи")} → {(inside ? "ВНУТРИ" : "СНАРУЖИ")}");
            }

            foreach (var clientId in outside)
            {
                var cat = CatController.FindByOwner(clientId);
                if (cat != null) SendToBench(cat);
            }

            EliminateGroup(outside);
            _insideReportedAt.Clear();

            if (Phase != MiniGamePhase.Playing) return;
            BoxVisible.Value = false;
            NextEventTime.Value = Now + hiddenTime;
        }

        /// <summary>Пауза прошла: коробка появляется в новом месте, меньше; раунд короче.</summary>
        private void ShowNextBox()
        {
            BoxSize.Value = Mathf.Max(minSize, BoxSize.Value * shrinkFactor);
            BoxCenter.Value = PickNewCenter(BoxCenter.Value, BoxSize.Value);
            BoxVisible.Value = true;

            Round.Value++;
            NextEventTime.Value = Now + _currentRoundTime;
            _currentRoundTime = Mathf.Max(minRoundTime, _currentRoundTime - roundTimeStep);
        }

        private Vector3 PickNewCenter(Vector3 current, float size)
        {
            float limit = Mathf.Max(0f, arenaHalfSize - size * 0.5f - 0.5f);

            Vector3 best = current;
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                float distance = Random.Range(minMoveDistance, maxMoveDistance);
                var candidate = new Vector3(
                    Mathf.Clamp(current.x + direction.x * distance, -limit, limit),
                    current.y,
                    Mathf.Clamp(current.z + direction.y * distance, -limit, limit));

                float actual = (candidate - current).magnitude;
                if (actual >= minMoveDistance) return candidate; // не упёрлись в край арены
                if (actual > bestDistance) { bestDistance = actual; best = candidate; }
            }

            return best;
        }

        private bool IsInside(Vector3 position)
        {
            Vector3 local = position - BoxCenter.Value;
            float half = BoxSize.Value * 0.5f - insideMargin;
            return Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half && local.y <= insideMaxHeight;
        }

        private void SendToBench(CatController cat)
        {
            if (bench == null) return;

            int column = _benchIndex % 4;
            int row = _benchIndex / 4;
            _benchIndex++;

            Vector3 position = bench.position
                               + bench.right * ((column - 1.5f) * benchSpacing)
                               + bench.forward * (row * benchSpacing);
            cat.SetRespawnAndTeleportRpc(position, bench.rotation);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportInsideRpc(bool inside, RpcParams rpcParams = default)
        {
            if (!BoxVisible.Value) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (inside) _insideReportedAt[sender] = Now;
            else _insideReportedAt.Remove(sender);
        }

        // ───────────────────────── Клиенты ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned) return;

            // Владелец кота сообщает серверу, в коробке ли он (раз в 0.15 c, пока коробка видна).
            if (Phase != MiniGamePhase.Playing || !BoxVisible.Value || Time.time < _nextReportTime) return;
            _nextReportTime = Time.time + 0.15f;

            ulong me = NetworkManager.LocalClientId;
            if (!IsAlive(me)) return;
            var cat = CatController.FindByOwner(me);
            bool inside = cat != null && IsInside(cat.transform.position);
            if (inside || _reportedInside)
            {
                ReportInsideRpc(inside);
                _reportedInside = inside;
            }
        }

        // Сервер меняет размер и место только пока коробка спрятана (и до старта), так что применяем сразу.
        private void OnSizeChanged(float previous, float next)
        {
            if (box != null) box.SetSize(next);
        }

        private void OnCenterChanged(Vector3 previous, Vector3 next)
        {
            if (box != null) box.SetPosition(next);
        }

        private void OnVisibleChanged(bool previous, bool next)
        {
            if (box == null) return;
            if (next)
            {
                // На случай, если значения пришли в другом порядке.
                box.SetSize(BoxSize.Value);
                box.SetPosition(BoxCenter.Value);
            }

            box.SetVisible(next, true);
        }
    }
}
