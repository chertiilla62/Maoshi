using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.HotPie
{
    /// <summary>
    /// «Горячий пирожок» — горячая картошка по-дворовому. Бабушка у лотка кидает раскалённый пирожок
    /// случайному коту. Пирожок висит у него над головой; передать — толкнуть лапой другого кота
    /// (пирожок перелетает к нему, а сам он на секунду оглушён — беги!). Вернуть пирожок тому, кто
    /// только что передал, сразу нельзя (1.5 с). Пирожок всё горячее (пар, дрожь, тиканье), и в случайный
    /// момент (12–20 с) — «пуф!»: кот, у которого он был, выбывает. Новый раунд — новый пирожок. Последний
    /// оставшийся — победитель. С пирожком кот бегает чуть быстрее (чтобы догнать).
    /// Сервер решает всё; клиенты видят держателя, время раунда и рисуют полёт/пар/взрыв (HotPieVisual).
    /// </summary>
    public class HotPieMode : MiniGameMode
    {
        public static HotPieMode Instance { get; private set; }

        [SerializeField] private Vector2 fuseSeconds = new(12f, 20f);
        [SerializeField] private float firstRoundDelay = 1.5f;
        [SerializeField] private float betweenRounds = 2.6f;
        [SerializeField] private float tossFlight = 0.7f;
        [SerializeField] private float passFlight = 0.25f;
        [SerializeField] private float noTagBack = 1.5f;
        [SerializeField] private float holderSpeed = 1.12f;
        [Tooltip("Арена: центр и радиус (кто вылетел за неё — возвращается внутрь).")]
        [SerializeField] private Transform bench;
        [SerializeField] private float benchSpacing = 1.4f;

        public const ulong Nobody = ulong.MaxValue;

        /// <summary>У кого пирожок (Nobody — между раундами).</summary>
        public readonly NetworkVariable<ulong> Holder = new(Nobody);
        /// <summary>От кого прилетел (для анимации полёта; Nobody — от бабушки).</summary>
        public readonly NetworkVariable<ulong> PreviousHolder = new(Nobody);
        /// <summary>Когда прилетит к держателю (до этого — в полёте).</summary>
        public readonly NetworkVariable<double> ArriveTime = new();
        /// <summary>Начало раунда и момент «пуф» (сервер знает точно; клиенты видят нагрев по долям).</summary>
        public readonly NetworkVariable<double> RoundStart = new();
        public readonly NetworkVariable<double> RoundEnd = new();
        public readonly NetworkVariable<int> Round = new();
        /// <summary>Номер взрыва (меняется — у всех «пуф» над Exploded).</summary>
        public readonly NetworkVariable<int> ExplodeSerial = new();
        public readonly NetworkVariable<ulong> Exploded = new(Nobody);

        private double _nextRoundAt = -1;
        private double _noTagBackUntil;
        private ulong _lastGiver = Nobody;
        private int _benchIndex;

        public double NowTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        /// <summary>Насколько раскалён пирожок: 0 — только испекли, 1 — сейчас пуф. По времени раунда, но
        /// конец раунда клиенты «не знают» точно: нагрев идёт по самой короткой возможной длине раунда.</summary>
        public float Heat
        {
            get
            {
                if (Holder.Value == Nobody) return 0f;
                double t = NowTime - RoundStart.Value;
                return Mathf.Clamp01((float)(t / fuseSeconds.x));
            }
        }

        /// <summary>Последние секунды: пирожок трясётся и тикает (за 3 с до конца — у всех).</summary>
        public bool Critical => Holder.Value != Nobody && NowTime > RoundEnd.Value - 3.0;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            Holder.OnValueChanged += OnHolderChanged;
            if (IsServer) CatController.ServerCatPushed += OnCatPushed;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            Holder.OnValueChanged -= OnHolderChanged;
            CatController.ServerCatPushed -= OnCatPushed;
            SetLocalSpeed(1f);
            base.OnNetworkDespawn();
        }

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Горячий пирожок: передай его толчком, пока не сделал «пуф»!";
                ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : Nobody;
                if (!IsAlive(me)) return "Ты выбыл — смотри, как печётся пирожок";
                ulong holder = Holder.Value;
                if (holder == Nobody) return $"Раунд {Round.Value + 1}: бабушка достаёт новый пирожок…";
                if (holder == me) return Critical ? "ГОРЯЧО! Скорее толкни кого-нибудь!" : "Пирожок у тебя — толкни другого кота, чтобы передать!";
                var player = Maoshi.Network.NetworkPlayer.FindByClientId(holder);
                string name = player != null ? player.DisplayName : "кого-то";
                return Critical ? $"Пирожок у {name} вот-вот сделает «пуф» — держись подальше!" : $"Пирожок у {name} — убегай!";
            }
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnGameStarted()
        {
            _nextRoundAt = Now + firstRoundDelay;
        }

        protected override void OnGameTick()
        {
            // Кто вылетел с площадки (упал за ограду) — обратно внутрь.
            if (Holder.Value == Nobody)
            {
                if (_nextRoundAt > 0 && Now >= _nextRoundAt) StartRound();
                return;
            }

            if (!IsAlive(Holder.Value))
            {
                // Держатель ушёл из игры — пирожок другому.
                GiveToRandom(Nobody);
                return;
            }

            if (Now >= RoundEnd.Value) Explode();
        }

        private void StartRound()
        {
            _nextRoundAt = -1;
            RoundStart.Value = Now;
            RoundEnd.Value = Now + Random.Range(fuseSeconds.x, fuseSeconds.y) + tossFlight;
            GiveToRandom(Nobody);
        }

        private void GiveToRandom(ulong exclude)
        {
            var candidates = new List<ulong>();
            foreach (ulong id in AlivePlayers) if (id != exclude && CatController.FindByOwner(id) != null) candidates.Add(id);
            if (candidates.Count == 0)
            {
                Holder.Value = Nobody;
                return;
            }
            PreviousHolder.Value = Nobody;
            ArriveTime.Value = Now + tossFlight;
            Holder.Value = candidates[Random.Range(0, candidates.Count)];
            _lastGiver = Nobody;
            TossRpc();
        }

        private void OnCatPushed(CatController attacker, CatController victim)
        {
            if (Phase != MiniGamePhase.Playing || Holder.Value == Nobody) return;
            ulong from = attacker.OwnerClientId, to = victim.OwnerClientId;
            if (from != Holder.Value || !IsAlive(to)) return;
            if (Now < ArriveTime.Value) return; // пирожок ещё летит
            if (to == _lastGiver && Now < _noTagBackUntil) return;

            _lastGiver = from;
            _noTagBackUntil = Now + noTagBack + passFlight;
            PreviousHolder.Value = from;
            ArriveTime.Value = Now + passFlight;
            Holder.Value = to;
            SessionStats.Add(from, Stat.Hits);
            PassRpc(victim.transform.position);
        }

        private void Explode()
        {
            ulong victim = Holder.Value;
            Exploded.Value = victim;
            ExplodeSerial.Value++;
            Holder.Value = Nobody;
            Round.Value++;

            var cat = CatController.FindByOwner(victim);
            if (cat != null)
            {
                cat.ReceiveHitRpc(Vector3.up * 5f + Random.insideUnitSphere * 1.5f, true);
                StartCoroutine(BenchLater(cat));
            }
            Eliminate(victim);
            if (Phase == MiniGamePhase.Playing) _nextRoundAt = Now + betweenRounds;
        }

        private System.Collections.IEnumerator BenchLater(CatController cat)
        {
            yield return new WaitForSeconds(1.3f);
            if (cat == null || bench == null) yield break;
            int column = _benchIndex % 4, row = _benchIndex / 4;
            _benchIndex++;
            Vector3 position = bench.position + bench.right * ((column - 1.5f) * benchSpacing) + bench.forward * (row * benchSpacing);
            cat.SetRespawnAndTeleportRpc(position, bench.rotation);
        }

        protected override void OnGameFinished()
        {
            Holder.Value = Nobody;
        }

        // ───────────────────────── Все ─────────────────────────

        /// <summary>С пирожком свой кот бегает чуть быстрее (догнать и передать).</summary>
        private void OnHolderChanged(ulong previous, ulong next)
        {
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : Nobody;
            SetLocalSpeed(next == me ? holderSpeed : 1f);
        }

        private static void SetLocalSpeed(float value)
        {
            var nm = NetworkManager.Singleton;
            var local = nm != null ? CatController.FindByOwner(nm.LocalClientId) : null;
            if (local != null) local.SpeedMultiplier = value;
        }

        [Rpc(SendTo.Everyone)]
        private void TossRpc() => AudioManager.Play("whistle", 0.4f);

        [Rpc(SendTo.Everyone)]
        private void PassRpc(Vector3 at) => AudioManager.PlayAt("pass", at, 0.9f);
    }
}
