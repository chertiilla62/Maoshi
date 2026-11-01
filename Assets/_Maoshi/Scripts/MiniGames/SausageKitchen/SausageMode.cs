using System;
using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Maoshi.MiniGames.SausageKitchen
{
    /// <summary>Что кот несёт в зубах.</summary>
    public struct SausageCarry : INetworkSerializable, IEquatable<SausageCarry>
    {
        public ulong ClientId;
        /// <summary>1 — кусок (+1), 2 — целая «Докторская» (+3).</summary>
        public byte Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Kind);
        }

        public bool Equals(SausageCarry other) => ClientId == other.ClientId && Kind == other.Kind;
    }

    public enum HostessState : byte { Cooking, Warning, Turning, Watching, TurningBack }

    /// <summary>
    /// «Колбаса с кухни» — «Море волнуется» с котами.
    /// Хозяйка стоит у плиты спиной к столу, на столе тарелка с колбасой. Толчок лапой (F) у тарелки — схватить кусок,
    /// донести до своей миски у дальней стены — +1. Иногда на тарелке лежит целая «Докторская» — +3.
    /// Хозяйка напевает (♪) и оборачивается. Кто в это время двигается у неё на виду — получает полотенцем:
    /// роняет колбасу и отправляется к своей миске. За коробками и шкафами не видно. Сбитый с лап кот (два толчка подряд)
    /// тоже роняет добычу — колбаса возвращается на тарелку.
    /// Сервер решает всё: состояние хозяйки, кто что несёт, кого заметили, очки.
    /// </summary>
    public class SausageMode : MiniGameMode, IMiniGameModal
    {
        public const byte Piece = 1;
        public const byte Stick = 2;

        [Header("Колбаса с кухни")]
        [SerializeField] private Hostess hostess;
        [SerializeField] private SausagePlate plate;
        [Tooltip("Миски (8 штук): расставляются в ряд по числу игроков, лишние прячутся.")]
        [SerializeField] private Transform[] bowls;
        [SerializeField] private float bowlZ = -5.2f;
        [SerializeField] private float bowlSpacing = 1.5f;
        [SerializeField] private float bowlRadius = 0.95f;
        [Tooltip("Шаблоны (выключенные объекты): кусок и целая палка колбасы — в зубах у кота и в миске.")]
        [SerializeField] private GameObject pieceTemplate;
        [SerializeField] private GameObject stickTemplate;

        [Header("Хозяйка")]
        [SerializeField] private Vector2 cookingSeconds = new(3f, 6.5f);
        [SerializeField] private Vector2 warningSeconds = new(1.0f, 0.65f);
        [SerializeField] private float turnSeconds = 0.3f;
        [SerializeField] private Vector2 watchSeconds = new(2f, 3.2f);
        [Tooltip("После поворота — время замереть.")]
        [SerializeField] private float graceSeconds = 0.35f;
        [Tooltip("Быстрее этого (м/с) — «двигается».")]
        [SerializeField] private float moveThreshold = 0.75f;
        [SerializeField, Range(0f, 1f)] private float fakeChance = 0.18f;
        [SerializeField] private float stickInterval = 16f;

        public readonly NetworkVariable<HostessState> State = new(HostessState.Cooking);
        public readonly NetworkVariable<double> StateSince = new();
        public readonly NetworkVariable<bool> StickOnPlate = new();
        public NetworkList<SausageCarry> Carrying;
        /// <summary>Миска i принадлежит BowlOwners[i]; миски стоят в ряд по их числу.</summary>
        public NetworkList<ulong> BowlOwners;

        // Сервер.
        private double _stateEnd;
        private bool _fake;
        private double _nextStick;
        private readonly Dictionary<ulong, Vector3> _lastPosition = new();
        private readonly Dictionary<ulong, float> _speed = new();
        /// <summary>Кого недавно отогнали: пока телепорт идёт по сети, кот ещё «бежит» — не ловим его второй раз.</summary>
        private readonly Dictionary<ulong, double> _caughtAt = new();
        [Tooltip("Через сколько секунд после «полотенца» кота снова можно поймать (в том же взгляде тоже).")]
        [SerializeField] private float recatchDelay = 0.9f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // Клиенты.
        private readonly Dictionary<ulong, GameObject> _carryVisuals = new();
        private readonly Dictionary<ulong, byte> _carryKinds = new();
        private readonly List<GameObject>[] _bowlPiles = new List<GameObject>[8];
        private int _bowlLayoutCount = -1;
        private HostessState _lastState;
        private double _nextSizzle;

        protected override bool UsesScores => true;

        public bool IsCarrying(ulong clientId) => KindOf(clientId) != 0;

        public byte KindOf(ulong clientId)
        {
            foreach (var entry in Carrying) if (entry.ClientId == clientId) return entry.Kind;
            return 0;
        }

        public float StateAge => (float)(NetworkManager != null ? NetworkManager.ServerTime.Time - StateSince.Value : 0);

        protected override void Awake()
        {
            base.Awake();
            Carrying = new NetworkList<SausageCarry>();
            BowlOwners = new NetworkList<ulong>();
            if (pieceTemplate != null) pieceTemplate.SetActive(false);
            if (stickTemplate != null) stickTemplate.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (plate != null) plate.Mode = this;
            if (hostess != null) hostess.Mode = this;
            State.OnValueChanged += OnStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            base.OnNetworkDespawn();
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            // Порядок мисок = порядок котов у спавнера (оба по возрастанию id): каждый появляется у своей миски.
            BowlOwners.Clear();
            foreach (var id in Participants) BowlOwners.Add(id);
            Carrying.Clear();
            StickOnPlate.Value = false;
            SetState(HostessState.Cooking, 999);
        }

        protected override void OnGameStarted()
        {
            SetState(HostessState.Cooking, Random.Range(cookingSeconds.x, cookingSeconds.y) + 1.5f);
            _nextStick = Now + stickInterval * 0.6f;
        }

        protected override void OnGameTick()
        {
            TrackSpeeds();
            TickHostess();

            if (State.Value == HostessState.Watching && Now - StateSince.Value >= graceSeconds) LookForMovers();

            // Несущие колбасу: сбит с лап — уронил, дошёл до миски — очки.
            for (int i = Carrying.Count - 1; i >= 0; i--)
            {
                var entry = Carrying[i];
                var cat = CatController.FindByOwner(entry.ClientId);
                if (cat == null || cat.State.Value == CatState.Knocked)
                {
                    Drop(i);
                    continue;
                }

                Vector3 bowl = BowlPosition(BowlIndex(entry.ClientId));
                Vector3 offset = cat.transform.position - bowl;
                offset.y = 0f;
                if (offset.magnitude <= bowlRadius)
                {
                    int points = entry.Kind == Stick ? 3 : 1;
                    AddScore(entry.ClientId, points);
                    Carrying.RemoveAt(i);
                    DeliveredRpc(bowl, entry.Kind);
                    Debug.Log($"[Maoshi] Колбаса: кот {entry.ClientId} донёс до миски (+{points}).");
                }
            }

            if (!StickOnPlate.Value && Now >= _nextStick && !AnyStickCarried())
            {
                StickOnPlate.Value = true;
                StickRpc();
            }
        }

        private void SetState(HostessState state, float seconds)
        {
            StateSince.Value = Now;
            _stateEnd = Now + seconds;
            State.Value = state;
        }

        private void TickHostess()
        {
            if (Now < _stateEnd) return;
            float p = Mathf.Clamp01((float)(ElapsedPlaying / Mathf.Max(1f, TimeLimit)));

            switch (State.Value)
            {
                case HostessState.Cooking:
                    _fake = p > 0.2f && Random.value < fakeChance;
                    SetState(HostessState.Warning, Mathf.Lerp(warningSeconds.x, warningSeconds.y, p));
                    break;
                case HostessState.Warning:
                    // Иногда только напевает и не оборачивается — чтобы не расслаблялись.
                    if (_fake) SetState(HostessState.Cooking, CookingTime(p));
                    else SetState(HostessState.Turning, turnSeconds);
                    break;
                case HostessState.Turning:
                    SetState(HostessState.Watching, Random.Range(watchSeconds.x, watchSeconds.y));
                    break;
                case HostessState.Watching:
                    SetState(HostessState.TurningBack, turnSeconds);
                    break;
                case HostessState.TurningBack:
                    SetState(HostessState.Cooking, CookingTime(p));
                    break;
            }
        }

        private float CookingTime(float progress) => Random.Range(cookingSeconds.x, cookingSeconds.y) * Mathf.Lerp(1f, 0.6f, progress);

        private void TrackSpeeds()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            foreach (var id in Participants)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                Vector3 position = cat.transform.position;
                if (_lastPosition.TryGetValue(id, out var last))
                {
                    float step = (position - last).magnitude;
                    // Скачок больше 1.5 м за кадр — телепорт (к миске, возврат), а не бег.
                    float frame = step > 1.5f ? 0f : Mathf.Min(step / dt, 30f);
                    float smoothed = _speed.TryGetValue(id, out float s) ? s : 0f;
                    _speed[id] = Mathf.Lerp(smoothed, frame, 1f - Mathf.Exp(-12f * dt));
                }
                _lastPosition[id] = position;
            }
        }

        private void LookForMovers()
        {
            if (hostess == null) return;
            foreach (var id in Participants)
            {
                if (_caughtAt.TryGetValue(id, out double at) && Now - at < recatchDelay) continue;
                var cat = CatController.FindByOwner(id);
                if (cat == null || cat.State.Value != CatState.Normal) continue;
                if (!_speed.TryGetValue(id, out float speed) || speed < moveThreshold) continue;
                if (!CanSee(cat)) continue;
                Catch(cat);
            }
        }

        /// <summary>Видит ли хозяйка кота: перед ней и не спрятан за мебелью.</summary>
        public bool CanSee(CatController cat)
        {
            Vector3 eye = hostess.Eye;
            Vector3 forward = hostess.WatchForward;
            foreach (float height in new[] { 0.25f, 0.55f })
            {
                Vector3 target = cat.transform.position + Vector3.up * height;
                Vector3 direction = target - eye;
                float distance = direction.magnitude;
                if (distance < 0.01f) return true;
                direction /= distance;
                if (Vector3.Dot(new Vector3(direction.x, 0f, direction.z).normalized, forward) < 0.2f) continue;

                bool blocked = false;
                int count = Physics.RaycastNonAlloc(eye, direction, Hits, distance - 0.15f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var collider = Hits[i].collider;
                    if (collider.GetComponentInParent<CatController>() != null) continue;
                    if (collider.GetComponentInParent<SausagePlate>() != null) continue;
                    if (collider.GetComponentInParent<Hostess>() != null) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) return true;
            }
            return false;
        }

        private void Catch(CatController cat)
        {
            ulong id = cat.OwnerClientId;
            _caughtAt[id] = Now;
            // После телепорта сглаженная скорость ещё «помнит» рывок — сбрасываем, иначе поймаем снова зря.
            _speed[id] = 0f;
            _lastPosition.Remove(id);
            for (int i = Carrying.Count - 1; i >= 0; i--)
                if (Carrying[i].ClientId == id) Drop(i);
            SwatRpc(id, cat.transform.position);
            Debug.Log($"[Maoshi] Колбаса: хозяйка заметила кота {id} (скорость {_speed[id]:0.0} м/с).");
            Vector3 home = BowlPosition(BowlIndex(id)) + new Vector3(0f, 0.3f, 0.8f);
            cat.SetRespawnAndTeleportRpc(home, Quaternion.LookRotation(Vector3.forward));
        }

        /// <summary>Уронил — колбаса возвращается на тарелку.</summary>
        private void Drop(int index)
        {
            var entry = Carrying[index];
            Carrying.RemoveAt(index);
            if (entry.Kind == Stick) StickOnPlate.Value = true;
            var cat = CatController.FindByOwner(entry.ClientId);
            if (cat != null) DroppedRpc(cat.transform.position);
        }

        private bool AnyStickCarried()
        {
            foreach (var entry in Carrying) if (entry.Kind == Stick) return true;
            return false;
        }

        /// <summary>Сервер: кот толкнул тарелку лапой.</summary>
        public void TryGrab(ulong clientId)
        {
            if (!IsServer || Phase != MiniGamePhase.Playing || IsCarrying(clientId) || !Participants.Contains(clientId)) return;

            // Тянуть лапу к тарелке, когда хозяйка смотрит, — тоже движение: если видит — полотенцем.
            var grabber = CatController.FindByOwner(clientId);
            bool watching = State.Value == HostessState.Watching || State.Value == HostessState.Turning;
            if (watching && grabber != null && hostess != null && CanSee(grabber))
            {
                if (!_caughtAt.TryGetValue(clientId, out double at) || Now - at >= recatchDelay) Catch(grabber);
                return;
            }
            byte kind = Piece;
            if (StickOnPlate.Value)
            {
                kind = Stick;
                StickOnPlate.Value = false;
                _nextStick = Now + stickInterval;
            }
            Carrying.Add(new SausageCarry { ClientId = clientId, Kind = kind });
            GrabRpc(plate != null ? plate.transform.position : Vector3.zero);
            Debug.Log($"[Maoshi] Колбаса: кот {clientId} схватил {(kind == Stick ? "«Докторскую»" : "кусок")}.");
        }

        // ───────────────────────── Миски ─────────────────────────

        public int BowlIndex(ulong clientId)
        {
            for (int i = 0; i < BowlOwners.Count; i++) if (BowlOwners[i] == clientId) return i;
            return 0;
        }

        public Vector3 BowlPosition(int index)
        {
            int count = Mathf.Max(1, BowlOwners.Count);
            float x = (index - (count - 1) * 0.5f) * bowlSpacing;
            return new Vector3(x, 0f, bowlZ);
        }

        // ───────────────────────── Сеть: звуки ─────────────────────────

        [Rpc(SendTo.Everyone)]
        private void GrabRpc(Vector3 at) => AudioManager.PlayAt("chomp", at, 0.9f);

        [Rpc(SendTo.Everyone)]
        private void DeliveredRpc(Vector3 at, byte kind) => AudioManager.PlayAt(kind == Stick ? "fish_big" : "fish", at, 0.9f, 0.02f, 40f);

        [Rpc(SendTo.Everyone)]
        private void DroppedRpc(Vector3 at) => AudioManager.PlayAt("ouch", at, 0.7f);

        [Rpc(SendTo.Everyone)]
        private void StickRpc()
        {
            if (plate != null) AudioManager.PlayAt("clink", plate.transform.position, 0.7f, 0.05f, 40f);
        }

        [Rpc(SendTo.Everyone)]
        private void SwatRpc(ulong clientId, Vector3 at)
        {
            AudioManager.PlayAt("towel", at, 1f, 0.06f, 40f);
            if (hostess != null) hostess.Swat(at);
            if (NetworkManager != null && clientId == NetworkManager.LocalClientId) AudioManager.Play("hiss", 0.6f);
        }

        // ───────────────────────── Клиенты: картинка ─────────────────────────

        private void OnStateChanged(HostessState previous, HostessState next)
        {
            if (Phase != MiniGamePhase.Playing) return;
            if (next == HostessState.Warning && hostess != null) AudioManager.PlayAt("hum", hostess.Eye, 1f, 0.04f, 40f);
            if (next == HostessState.Watching) AudioManager.Play("countdown", 0.7f);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned) return;
            UpdateBowls();
            UpdateCarryVisuals();

            // Шкварчит сковородка, пока хозяйка готовит.
            if (Phase == MiniGamePhase.Playing && State.Value == HostessState.Cooking && Time.timeAsDouble >= _nextSizzle && hostess != null)
            {
                _nextSizzle = Time.timeAsDouble + 2.3;
                AudioManager.PlayAt("sizzle", hostess.Stove, 0.45f, 0.05f, 18f);
            }
        }

        private void UpdateBowls()
        {
            if (bowls == null) return;
            int count = BowlOwners.Count;
            if (count != _bowlLayoutCount)
            {
                _bowlLayoutCount = count;
                for (int i = 0; i < bowls.Length; i++)
                {
                    if (bowls[i] == null) continue;
                    bool used = i < count;
                    bowls[i].gameObject.SetActive(used);
                    if (!used) continue;
                    bowls[i].position = BowlPosition(i);
                    var player = NetworkPlayer.FindByClientId(BowlOwners[i]);
                    var rim = bowls[i].Find("Rim");
                    if (rim != null && player != null && rim.TryGetComponent(out Renderer renderer))
                        renderer.material.SetColor("_BaseColor", player.Color);
                }
            }

            // Горка колбасы в миске растёт с очками (до 7 кусочков).
            for (int i = 0; i < count && i < bowls.Length; i++)
            {
                if (bowls[i] == null || pieceTemplate == null) continue;
                _bowlPiles[i] ??= new List<GameObject>();
                var pile = _bowlPiles[i];
                int want = Mathf.Min(7, GetScore(BowlOwners[i]));
                while (pile.Count < want)
                {
                    int k = pile.Count;
                    var piece = Instantiate(pieceTemplate, bowls[i]);
                    piece.SetActive(true);
                    float a = k * 2.4f;
                    piece.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.12f * (k > 0 ? 1 : 0), 0.08f + k * 0.025f, Mathf.Sin(a) * 0.12f * (k > 0 ? 1 : 0));
                    piece.transform.localRotation = Quaternion.Euler(0f, k * 67f, 0f);
                    pile.Add(piece);
                }
                while (pile.Count > want)
                {
                    Destroy(pile[^1]);
                    pile.RemoveAt(pile.Count - 1);
                }
            }
        }

        private void UpdateCarryVisuals()
        {
            // Убрать лишние.
            var stale = new List<ulong>();
            foreach (var pair in _carryVisuals)
            {
                byte kind = KindOf(pair.Key);
                if (kind == 0 || kind != _carryKinds[pair.Key]) stale.Add(pair.Key);
            }
            foreach (var id in stale)
            {
                if (_carryVisuals[id] != null) Destroy(_carryVisuals[id]);
                _carryVisuals.Remove(id);
                _carryKinds.Remove(id);
            }

            foreach (var entry in Carrying)
            {
                var cat = CatController.FindByOwner(entry.ClientId);
                if (cat == null) continue;
                if (!_carryVisuals.TryGetValue(entry.ClientId, out var visual) || visual == null)
                {
                    var template = entry.Kind == Stick ? stickTemplate : pieceTemplate;
                    if (template == null) continue;
                    visual = Instantiate(template);
                    visual.SetActive(true);
                    _carryVisuals[entry.ClientId] = visual;
                    _carryKinds[entry.ClientId] = entry.Kind;
                }
                // В зубах, поперёк морды.
                var t = cat.transform;
                visual.transform.SetPositionAndRotation(t.position + t.up * 0.36f + t.forward * 0.42f, t.rotation * Quaternion.Euler(0f, 90f, 0f));
            }
        }

        public override void OnDestroy()
        {
            foreach (var visual in _carryVisuals.Values) if (visual != null) Destroy(visual);
            _carryVisuals.Clear();
            base.OnDestroy();
        }

        // ───────────────────────── Подсказки ─────────────────────────

        private ulong LocalId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Хватай колбасу со стола лапой (F) и неси в свою миску. Хозяйка обернётся — замри!";
                string stick = StickOnPlate.Value ? " На столе целая «Докторская» — +3!" : "";
                switch (State.Value)
                {
                    case HostessState.Turning:
                    case HostessState.Watching:
                        return "Хозяйка смотрит — замри! За коробками и шкафом не видно.";
                    case HostessState.Warning:
                        return "Хозяйка напевает — сейчас обернётся!";
                }
                byte kind = KindOf(LocalId);
                if (kind == Stick) return "Несёшь «Докторскую» — тащи в свою миску (+3)!";
                if (kind == Piece) return "Несёшь колбасу — тащи в свою миску!" + stick;
                return "Хозяйка у плиты — хватай колбасу со стола (F)!" + stick;
            }
        }

        public string ModalKey =>
            Phase == MiniGamePhase.Playing && (State.Value == HostessState.Watching || State.Value == HostessState.Turning) ? "sausage-freeze" : "";

        public VisualElement BuildModal()
        {
            var sign = UIKit.Box("freeze-sign");
            sign.pickingMode = PickingMode.Ignore;
            sign.Add(UIKit.Text("Замри!", "freeze-sign__title"));
            sign.Add(UIKit.Text("хозяйка смотрит", "freeze-sign__sub"));
            return sign;
        }
    }
}
