using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Session;
using UnityEngine;

namespace Maoshi.MiniGames.JanitorEscape
{
    /// <summary>
    /// «Побег от дворника»: двор, по которому носится дворник с метлой. Кого замёл — выбыл.
    /// Последний непойманный получает больше всех рыбы (места — по порядку выбывания).
    /// Со временем дворник бегает быстрее, а из дворницкой выходят его напарники.
    /// Подлянка: толкни соперника под метлу.
    /// </summary>
    public class JanitorEscapeMode : MiniGameMode
    {
        [Header("Дворники")]
        [SerializeField] private Janitor[] janitors;
        [Tooltip("Через сколько секунд после старта выходит первый дворник — котам нужно время разбежаться.")]
        [SerializeField] private float firstJanitorDelay = 2.5f;
        [Tooltip("На какой секунде игры выходит 2-й, 3-й… дворник.")]
        [SerializeField] private float[] extraJanitorTimes = { 25f, 50f };
        [Tooltip("Минимум игроков в партии, чтобы вышел 2-й, 3-й… дворник.")]
        [SerializeField] private int[] extraJanitorMinPlayers = { 1, 4 };
        [Tooltip("Скорость дворников растёт от 1× до этого множителя за speedRampTime.")]
        [SerializeField] private float maxSpeedMultiplier = 1.35f;
        [SerializeField] private float speedRampTime = 60f;

        [Header("Пойманные")]
        [SerializeField] private float caughtFling = 4.5f;
        [SerializeField] private float caughtUp = 4f;
        [Tooltip("Через сколько секунд пойманного кота уносят на скамейку.")]
        [SerializeField] private float benchDelay = 1.2f;
        [SerializeField] private Transform bench;
        [SerializeField] private float benchSpacing = 1.5f;

        private int _benchIndex;
        private int _activeJanitors;
        private readonly List<(ulong clientId, double at)> _toBench = new();

        public override string StatusText =>
            Phase == MiniGamePhase.Playing
                ? $"Беги от дворника! Котов на свободе: {AliveCount}"
                : "Дворник выходит на смену. Разбегайтесь!";

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            if (janitors == null) return;
            foreach (var janitor in janitors)
            {
                if (janitor == null) continue;
                janitor.Swept -= OnSwept;
                janitor.Swept += OnSwept;
            }
        }

        protected override void OnGameTick()
        {
            if (janitors == null) return;

            // Первый дворник — после короткой паузы, дальше больше дворников со временем.
            if (_activeJanitors == 0 && ElapsedPlaying >= firstJanitorDelay) ActivateNext();
            int nextExtra = _activeJanitors - 1;
            if (nextExtra >= 0 && nextExtra < extraJanitorTimes.Length && _activeJanitors < janitors.Length
                && ElapsedPlaying >= extraJanitorTimes[nextExtra]
                && Participants.Count >= MinPlayersFor(nextExtra))
            {
                ActivateNext();
            }

            // Быстрее со временем.
            float speed = Mathf.Lerp(1f, maxSpeedMultiplier, Mathf.Clamp01((float)(ElapsedPlaying / speedRampTime)));
            foreach (var janitor in janitors)
                if (janitor != null) janitor.SpeedMultiplier = speed;

            TickBench();
        }

        protected override void Update()
        {
            base.Update();
            // Скамейку доводим и после финиша (последнего пойманного тоже надо унести).
            if (IsSpawned && IsServer && Phase == MiniGamePhase.Finished) TickBench();
        }

        protected override void OnGameFinished()
        {
            if (janitors == null) return;
            foreach (var janitor in janitors)
                if (janitor != null) janitor.Stop();
        }

        private int MinPlayersFor(int extraIndex) =>
            extraIndex < extraJanitorMinPlayers.Length ? extraJanitorMinPlayers[extraIndex] : 1;

        private void ActivateNext()
        {
            if (_activeJanitors >= janitors.Length) return;
            var janitor = janitors[_activeJanitors++];
            if (janitor == null) return;
            janitor.Activate(IsAlive, IsTakenByOther);
            Debug.Log($"[Maoshi] Дворник №{_activeJanitors} вышел на смену.");
        }

        /// <summary>Этого кота уже гонит другой дворник?</summary>
        private bool IsTakenByOther(Janitor self, ulong clientId)
        {
            foreach (var janitor in janitors)
            {
                if (janitor != null && janitor != self && janitor.IsActive && janitor.TargetId == clientId) return true;
            }

            return false;
        }

        private void OnSwept(Janitor janitor, List<CatController> caught)
        {
            if (Phase != MiniGamePhase.Playing || caught.Count == 0) return;

            var group = new List<ulong>();
            foreach (var cat in caught)
            {
                if (cat == null || !IsAlive(cat.OwnerClientId)) continue;

                // Метла отправляет кота в полёт, потом его уносят на скамейку.
                Vector3 away = Vector3.ProjectOnPlane(cat.transform.position - janitor.transform.position, Vector3.up);
                if (away.sqrMagnitude < 0.01f) away = janitor.transform.forward;
                cat.ReceiveHitRpc(away.normalized * caughtFling + Vector3.up * caughtUp, true);

                group.Add(cat.OwnerClientId);
                _toBench.Add((cat.OwnerClientId, Now + benchDelay));
            }

            // Пойманные одним взмахом делят место.
            if (group.Count > 0) EliminateGroup(group);
        }

        private void TickBench()
        {
            for (int i = _toBench.Count - 1; i >= 0; i--)
            {
                if (Now < _toBench[i].at) continue;
                var cat = CatController.FindByOwner(_toBench[i].clientId);
                _toBench.RemoveAt(i);
                if (cat != null) SendToBench(cat);
            }
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
    }
}
