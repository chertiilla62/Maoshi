using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.FlyHunt
{
    /// <summary>
    /// «Охота на муху»: над двором летает одна муха — то низко, то высоко, иногда присаживается на ящик.
    /// Кто поймал (допрыгнул), получает очко; муха исчезает и через секунду появляется в другом месте.
    /// Все прыгают за одной целью и сбивают друг друга в воздухе. По таймеру места считаются по очкам.
    ///
    /// Сеть: полётом управляет сервер (позиция — NetworkVariable, клиенты сглаживают).
    /// Поймал ли — замечает владелец кота и шлёт заявку, сервер проверяет с запасом на задержку (как с мышами).
    /// </summary>
    public class FlyHuntMode : MiniGameMode
    {
        [Header("Муха")]
        [SerializeField] private FlyVisual fly;
        [Tooltip("Поймал, если муха ближе этого к «голове» кота.")]
        [SerializeField] private float catchRadius = 0.75f;
        [Tooltip("Высота «головы» над точкой опоры кота.")]
        [SerializeField] private float catHeadHeight = 0.4f;
        [Tooltip("Запас к радиусу при проверке заявки на сервере.")]
        [SerializeField] private float serverTolerance = 1.3f;

        [Header("Полёт")]
        [SerializeField] private float arenaHalfSize = 6.5f;
        [SerializeField] private float minHeight = 0.6f;
        [SerializeField] private float maxHeight = 1.9f;
        [SerializeField] private float startSpeed = 2.2f;
        [SerializeField] private float endSpeed = 3.6f;
        [Tooltip("За сколько секунд муха разгоняется от начальной скорости до конечной.")]
        [SerializeField] private float speedRampTime = 60f;
        [Tooltip("Шанс, что следующей точкой будет ящик, на котором муха посидит.")]
        [SerializeField, Range(0f, 1f)] private float perchChance = 0.3f;
        [SerializeField] private float perchTime = 1.2f;
        [SerializeField] private Transform[] perches;
        [Tooltip("Через сколько секунд после поимки муха появляется снова.")]
        [SerializeField] private float respawnDelay = 0.9f;
        [SerializeField] private float respawnMinDistance = 4f;

        public readonly NetworkVariable<Vector3> FlyPosition = new();
        public readonly NetworkVariable<bool> FlyVisible = new(true);
        /// <summary>Номер мухи: растёт при каждой поимке — чтобы заявка на уже пойманную муху не засчиталась.</summary>
        public readonly NetworkVariable<int> FlyId = new();

        // Сервер.
        private Vector3 _target;
        private float _perchUntil;
        private bool _perching;
        private bool _perchTarget;
        private double _respawnAt;
        private float _wobblePhase;

        // Клиент.
        private float _nextClaimTime;
        private int _claimedFlyId = -1;

        protected override bool UsesScores => true;
        public override string StatusText => FlyVisible.Value ? "Лови муху! Прыгай и толкайся" : "Муха улетела… сейчас вернётся";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            FlyVisible.OnValueChanged += OnVisibleChanged;
            FlyPosition.OnValueChanged += OnPositionChanged;

            if (IsServer && fly != null)
            {
                FlyPosition.Value = fly.transform.position;
                _target = FlyPosition.Value;
            }

            if (fly != null)
            {
                fly.Snap(FlyPosition.Value);
                fly.SetVisible(FlyVisible.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            FlyVisible.OnValueChanged -= OnVisibleChanged;
            FlyPosition.OnValueChanged -= OnPositionChanged;
            base.OnNetworkDespawn();
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnGameStarted()
        {
            PickNextTarget();
        }

        protected override void OnGameTick()
        {
            if (!FlyVisible.Value)
            {
                if (Now >= _respawnAt) Respawn();
                return;
            }

            float dt = Time.deltaTime;
            Vector3 position = FlyPosition.Value;

            if (_perching)
            {
                if (Time.time >= _perchUntil) { _perching = false; PickNextTarget(); }
                return;
            }

            // Скорость растёт к концу игры.
            float progress = Mathf.Clamp01((float)ElapsedPlaying / speedRampTime);
            float speed = Mathf.Lerp(startSpeed, endSpeed, progress);

            Vector3 next = Vector3.MoveTowards(position, _target, speed * dt);
            // Лёгкое «жужжание»: муха чуть рыщет вверх-вниз, а не летит по линейке.
            _wobblePhase += dt * 7f;
            next.y += Mathf.Sin(_wobblePhase) * 0.25f * dt * speed;
            FlyPosition.Value = next;

            if ((next - _target).sqrMagnitude < 0.01f)
            {
                if (_perchTarget) { _perching = true; _perchUntil = Time.time + perchTime; }
                else PickNextTarget();
            }
        }

        private void PickNextTarget()
        {
            _perchTarget = perches != null && perches.Length > 0 && Random.value < perchChance;
            if (_perchTarget)
            {
                var perch = perches[Random.Range(0, perches.Length)];
                _target = perch.position + Vector3.up * 0.15f;
                return;
            }

            Vector3 current = FlyPosition.Value;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var candidate = new Vector3(
                    Random.Range(-arenaHalfSize, arenaHalfSize),
                    Random.Range(minHeight, maxHeight),
                    Random.Range(-arenaHalfSize, arenaHalfSize));
                _target = candidate;
                if ((candidate - current).magnitude > 2.5f) break; // короткие перелёты скучные
            }
        }

        /// <summary>Сервер: точка появления подальше (по горизонтали) от поймавшего.</summary>
        private Vector3 PickRespawnPosition(Vector3 avoid)
        {
            Vector3 position = FlyPosition.Value;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                position = new Vector3(
                    Random.Range(-arenaHalfSize, arenaHalfSize),
                    Random.Range(minHeight, maxHeight),
                    Random.Range(-arenaHalfSize, arenaHalfSize));
                Vector3 flat = position - avoid;
                flat.y = 0f;
                if (flat.magnitude >= respawnMinDistance) break;
            }

            return position;
        }

        private void Respawn()
        {
            FlyPosition.Value = _respawnPosition;
            _perching = false;
            PickNextTarget();
            FlyVisible.Value = true;
        }

        protected override void OnGameFinished()
        {
            FlyVisible.Value = false;
        }

        /// <summary>Заявка владельца кота: «я поймал муху номер flyId».</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CatchRpc(int flyId, RpcParams rpcParams = default)
        {
            if (Phase != MiniGamePhase.Playing || !FlyVisible.Value || flyId != FlyId.Value) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsAlive(sender)) return;

            var cat = CatController.FindByOwner(sender);
            if (cat == null) return;
            Vector3 head = cat.transform.position + Vector3.up * catHeadHeight;
            float limit = catchRadius + serverTolerance;
            if ((head - FlyPosition.Value).sqrMagnitude > limit * limit) return;

            AddScore(sender, 1);
            FlyId.Value++;
            FlyVisible.Value = false;
            _respawnAt = Now + respawnDelay;
            // Следующая муха — подальше от поймавшего, чтобы не ловил подряд.
            _respawnPosition = PickRespawnPosition(cat.transform.position);
        }

        private Vector3 _respawnPosition;

        // ───────────────────────── Клиенты ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned || Phase != MiniGamePhase.Playing || !FlyVisible.Value || fly == null) return;
            if (Time.time < _nextClaimTime || _claimedFlyId == FlyId.Value) return;

            ulong me = NetworkManager.LocalClientId;
            if (!IsAlive(me)) return;
            var cat = CatController.FindByOwner(me);
            if (cat == null) return;

            // Сравниваем с мухой, которую видит игрок (сглаженной) — так честно для него.
            Vector3 head = cat.transform.position + Vector3.up * catHeadHeight;
            if ((head - fly.transform.position).sqrMagnitude > catchRadius * catchRadius) return;

            _claimedFlyId = FlyId.Value;
            _nextClaimTime = Time.time + 0.2f;
            CatchRpc(FlyId.Value);
        }

        private void OnPositionChanged(Vector3 previous, Vector3 next)
        {
            if (fly != null) fly.SetTarget(next);
        }

        private void OnVisibleChanged(bool previous, bool next)
        {
            if (fly == null) return;
            if (next) fly.Snap(FlyPosition.Value);
            fly.SetVisible(next);
        }
    }
}
