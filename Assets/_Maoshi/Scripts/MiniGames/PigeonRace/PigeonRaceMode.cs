using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.PigeonRace
{
    /// <summary>
    /// «Гонка по крышам за голубем»: трасса из крыш вдоль +Z с провалами, ловушками и паркуром.
    /// Голубь летит впереди и показывает дорогу. Это гонка на места: кто первым добежал до голубятни,
    /// тот хватает голубя и занимает 1-е место, дальше — по порядку прибежавших. После первого финиша
    /// остальным даётся afterCatchTime секунд; не успевшие — по тому, кто дальше продвинулся.
    /// Упал в провал — не выбыл, а вернулся на начало последней пройденной крыши.
    /// </summary>
    public class PigeonRaceMode : MiniGameMode
    {
        public enum PigeonState : byte { Perched, Flying, Caught }

        [Header("Трасса")]
        [Tooltip("Начала крыш по порядку — сюда возвращаются упавшие.")]
        [SerializeField] private Transform[] checkpoints;
        [Tooltip("Ниже этой высоты кот считается упавшим с крыш.")]
        [SerializeField] private float fallY = 5f;
        [Tooltip("Финиш: кот на последней крыше дальше этой отметки по Z.")]
        [SerializeField] private float finishZ = 63f;
        [SerializeField] private float finishMinY = 7.5f;
        [Tooltip("После того как голубя схватили, остальным даётся столько секунд добежать.")]
        [SerializeField] private float afterCatchTime = 20f;

        [Header("Голубь")]
        [SerializeField] private PigeonVisual pigeon;
        [Tooltip("Насесты по порядку, последний — у голубятни.")]
        [SerializeField] private Transform[] perches;
        [Tooltip("Подбежал ближе — голубь улетает на следующий насест.")]
        [SerializeField] private float scareRadius = 3.2f;
        [SerializeField] private float flySpeed = 9f;
        [SerializeField] private float flyArcHeight = 2.5f;

        public readonly NetworkVariable<Vector3> PigeonPosition = new();
        public readonly NetworkVariable<PigeonState> Pigeon = new(PigeonState.Perched);
        public readonly NetworkVariable<ulong> CatcherId = new(ulong.MaxValue);
        public readonly NetworkVariable<int> FinishedCount = new();
        public readonly NetworkVariable<double> CatchDeadline = new();
        /// <summary>Порядок финиша (у всех клиентов — для «твоего места» в подсказке).</summary>
        public NetworkList<ulong> Finishers;

        protected override void Awake()
        {
            Finishers = new NetworkList<ulong>();
            base.Awake();
        }

        // Сервер.
        private int _perchIndex;
        private Vector3 _flyFrom;
        private Vector3 _flyTo;
        private float _flyDuration;
        private double _flyStart;
        private readonly List<ulong> _finishOrder = new();
        private readonly Dictionary<ulong, float> _bestZ = new();
        private readonly Dictionary<ulong, double> _respawnUntil = new();

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Гонка на места: первый у голубятни хватает голубя! Берегись ловушек. Упал — вернёшься назад.";
                string place = LocalPlaceText();
                if (CatcherId.Value == ulong.MaxValue) return $"Беги к голубятне в конце крыш!   {place}";
                float left = Mathf.Max(0f, (float)(CatchDeadline.Value - NetworkManager.ServerTime.Time));
                return $"Голубь схвачен! Добегайте до голубятни: {left:0} с   {place}";
            }
        }

        /// <summary>Клиент: «Ты финишировал 2-м» или текущее место по тому, кто дальше по трассе.</summary>
        private string LocalPlaceText()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return "";
            ulong me = nm.LocalClientId;
            int finishedIndex = Finishers.IndexOf(me);
            if (finishedIndex >= 0) return $"Ты финишировал {finishedIndex + 1}-м!";
            var mine = CatController.FindByOwner(me);
            if (mine == null) return "";
            int place = Finishers.Count + 1;
            int total = 0;
            foreach (var cat in CatController.All)
            {
                if (cat == null) continue;
                total++;
                if (cat == mine || Finishers.Contains(cat.OwnerClientId)) continue;
                if (cat.transform.position.z > mine.transform.position.z) place++;
            }
            return $"Место: {place} из {total}";
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            PigeonPosition.OnValueChanged += OnPigeonMoved;
            Pigeon.OnValueChanged += OnPigeonStateChanged;

            if (IsServer && perches != null && perches.Length > 0)
                PigeonPosition.Value = perches[0].position;

            if (pigeon != null)
            {
                pigeon.Snap(PigeonPosition.Value);
                pigeon.SetFlying(Pigeon.Value != PigeonState.Perched);
            }
        }

        public override void OnNetworkDespawn()
        {
            PigeonPosition.OnValueChanged -= OnPigeonMoved;
            Pigeon.OnValueChanged -= OnPigeonStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnPigeonMoved(Vector3 previous, Vector3 next)
        {
            if (pigeon != null) pigeon.SetTarget(next);
        }

        private void OnPigeonStateChanged(PigeonState previous, PigeonState next)
        {
            if (pigeon != null) pigeon.SetFlying(next != PigeonState.Perched);
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            _perchIndex = 0;
            if (perches != null && perches.Length > 0) PigeonPosition.Value = perches[0].position;
            Pigeon.Value = PigeonState.Perched;
            CatcherId.Value = ulong.MaxValue;
            FinishedCount.Value = 0;
            _finishOrder.Clear();
            Finishers.Clear();
            _bestZ.Clear();
            foreach (var clientId in Participants) _bestZ[clientId] = float.MinValue;
        }

        protected override void OnGameTick()
        {
            TickCats();
            TickPigeon();

            // Конец: все добежали или вышло время после поимки голубя.
            if (Phase != MiniGamePhase.Playing) return;
            if (_finishOrder.Count >= Participants.Count && Participants.Count > 0) FinishGame();
            else if (CatcherId.Value != ulong.MaxValue && Now >= CatchDeadline.Value) FinishGame();
        }

        private void TickCats()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                ulong clientId = Participants[i];
                var cat = CatController.FindByOwner(clientId);
                if (cat == null) continue;
                Vector3 position = cat.transform.position;

                // Упал в провал — на начало последней пройденной крыши.
                if (position.y < fallY)
                {
                    if (_respawnUntil.TryGetValue(clientId, out double until) && Now < until) continue;
                    _respawnUntil[clientId] = Now + 1.0; // пока телепорт доходит до владельца
                    Transform checkpoint = CheckpointFor(clientId);
                    if (checkpoint != null)
                    {
                        Vector3 spot = checkpoint.position + checkpoint.right * (((i % 4) - 1.5f) * 1.1f);
                        cat.SetRespawnAndTeleportRpc(spot, checkpoint.rotation);
                    }
                    continue;
                }

                // Прогресс считаем только на крышах.
                if (position.y > fallY + 2f && (!_bestZ.TryGetValue(clientId, out float best) || position.z > best))
                    _bestZ[clientId] = position.z;

                // Финиш у голубятни: место — по порядку прибежавших. Первый хватает голубя.
                if (!_finishOrder.Contains(clientId) && position.z >= finishZ && position.y >= finishMinY && Phase == MiniGamePhase.Playing)
                {
                    _finishOrder.Add(clientId);
                    Finishers.Add(clientId);
                    FinishedCount.Value = _finishOrder.Count;
                    if (CatcherId.Value == ulong.MaxValue) Catch(cat);
                }
            }
        }

        private Transform CheckpointFor(ulong clientId)
        {
            if (checkpoints == null || checkpoints.Length == 0) return null;
            float best = _bestZ.TryGetValue(clientId, out float z) ? z : float.MinValue;

            Transform result = checkpoints[0];
            foreach (var checkpoint in checkpoints)
            {
                if (checkpoint != null && checkpoint.position.z <= best + 0.5f) result = checkpoint;
            }

            return result;
        }

        private void TickPigeon()
        {
            if (perches == null || perches.Length == 0) return;

            switch (Pigeon.Value)
            {
                case PigeonState.Perched:
                    bool last = _perchIndex >= perches.Length - 1;
                    var nearest = NearestCat(PigeonPosition.Value, out float distance, out float dy);
                    if (nearest == null) break;

                    // Улетает, если кот подбежал близко или уже обогнал его (пробежал мимо сбоку).
                    if (!last && (distance < scareRadius || LeaderZ() > PigeonPosition.Value.z - 0.5f))
                        StartFlight(_perchIndex + 1);
                    // На последнем насесте голубь ждёт: его хватает первый финишировавший (см. TickCats).
                    break;

                case PigeonState.Flying:
                    float t = Mathf.Clamp01((float)((Now - _flyStart) / _flyDuration));
                    Vector3 position = Vector3.Lerp(_flyFrom, _flyTo, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * flyArcHeight);
                    PigeonPosition.Value = position;
                    if (t >= 1f) Pigeon.Value = PigeonState.Perched;
                    break;

                case PigeonState.Caught:
                    var catcher = CatController.FindByOwner(CatcherId.Value);
                    if (catcher != null) PigeonPosition.Value = catcher.transform.position + Vector3.up * 0.75f;
                    break;
            }
        }

        private void StartFlight(int perchIndex)
        {
            _perchIndex = perchIndex;
            _flyFrom = PigeonPosition.Value;
            _flyTo = perches[perchIndex].position;
            _flyDuration = Mathf.Max(0.4f, Vector3.Distance(_flyFrom, _flyTo) / flySpeed);
            _flyStart = Now;
            Pigeon.Value = PigeonState.Flying;
        }

        private void Catch(CatController cat)
        {
            ulong clientId = cat.OwnerClientId;
            CatcherId.Value = clientId;
            Pigeon.Value = PigeonState.Caught;

            double deadline = Now + afterCatchTime;
            if (TimeLeft > 0f) deadline = System.Math.Min(deadline, Now + TimeLeft);
            CatchDeadline.Value = deadline;
            Debug.Log($"[Maoshi] Голубя поймал игрок {clientId}.");
        }

        /// <summary>Самый дальний по трассе кот (только те, кто на крышах).</summary>
        private float LeaderZ()
        {
            float best = float.MinValue;
            foreach (var clientId in Participants)
            {
                var cat = CatController.FindByOwner(clientId);
                if (cat == null || cat.transform.position.y < fallY + 2f) continue;
                best = Mathf.Max(best, cat.transform.position.z);
            }

            return best;
        }

        private CatController NearestCat(Vector3 point, out float distance, out float dy)
        {
            CatController best = null;
            distance = float.MaxValue;
            dy = 0f;

            foreach (var clientId in Participants)
            {
                var cat = CatController.FindByOwner(clientId);
                if (cat == null) continue;
                Vector3 to = cat.transform.position + Vector3.up * 0.3f - point;
                float flat = new Vector2(to.x, to.z).magnitude;
                if (flat < distance)
                {
                    distance = flat;
                    dy = to.y;
                    best = cat;
                }
            }

            return best;
        }

        /// <summary>Места: по порядку финиша у голубятни, затем недобежавшие — кто дальше продвинулся.</summary>
        protected override List<Placement> BuildPlacements()
        {
            var result = new List<Placement>();
            int place = 1;

            foreach (var clientId in _finishOrder)
            {
                if (!Participants.Contains(clientId)) continue;
                result.Add(new Placement(clientId, place++));
            }

            var rest = new List<ulong>();
            foreach (var clientId in Participants)
                if (!_finishOrder.Contains(clientId)) rest.Add(clientId);
            rest.Sort((a, b) => BestZ(b).CompareTo(BestZ(a)));

            foreach (var clientId in rest)
                result.Add(new Placement(clientId, place++));

            return result;
        }

        private float BestZ(ulong clientId) => _bestZ.TryGetValue(clientId, out float z) ? z : float.MinValue;
    }
}
