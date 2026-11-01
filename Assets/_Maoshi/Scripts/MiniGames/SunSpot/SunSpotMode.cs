using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.SunSpot
{
    /// <summary>
    /// «Солнечное пятно»: в гостиной через большое окно падает луч солнца, пятно медленно ползёт
    /// по подоконнику, полу, дивану, креслу, когтеточке… Кот, который греется в пятне, копит очки
    /// (1 в секунду; если в пятне несколько котов — делят). Толкай соперников из пятна.
    /// - Иногда находит облако: пятно гаснет на пару секунд и выглядывает в другом месте.
    /// - Хозяйка у двери брызгает из пульверизатора в лидера: под котом появляется синий круг,
    ///   через секунду — пшик. Мокрый кот 3 с не греется (и его на секунду сбивает с лап).
    /// - Последние 20 секунд — «последние лучи»: очки вдвое.
    ///
    /// Луч — цилиндр от точки в окне (WindowPoint) через точку на маршруте (SpotPoint). Кот «на солнце»,
    /// если он внутри цилиндра и между ним и окном нет мебели (луч не загорожен). Всё это считают и сервер
    /// (очки), и клиенты (подсказка, картинка) по одному и тому же сетевому времени — синхронизировать
    /// положение пятна не нужно, только начало отрезка пути и облака.
    /// </summary>
    public class SunSpotMode : MiniGameMode
    {
        public static SunSpotMode Instance { get; private set; }

        [Header("Пятно")]
        [Tooltip("Маршрут пятна (точки на поверхностях), замкнутый.")]
        [SerializeField] private Vector3[] path;
        [SerializeField] private float speed = 0.8f;
        [SerializeField] private float radius = 0.95f;
        [Tooltip("Окно: центр проёма по высоте, плоскость стены (z), половина ширины, куда может смещаться луч.")]
        [SerializeField] private float windowY = 1.85f;
        [SerializeField] private float windowZ = 5.4f;
        [SerializeField] private float windowHalfX = 2f;
        [SerializeField] private float pointsPerSecond = 1f;
        [SerializeField] private float finalBoostSeconds = 20f;

        [Header("Облака")]
        [SerializeField] private float firstCloud = 14f;
        [SerializeField] private Vector2 cloudInterval = new(13f, 19f);
        [SerializeField] private float cloudDuration = 2.4f;

        [Header("Хозяйка")]
        [SerializeField] private float firstSpray = 11f;
        [SerializeField] private Vector2 sprayInterval = new(6.5f, 9.5f);
        [SerializeField] private float sprayWarning = 1.0f;
        [SerializeField] private float sprayRadius = 1.1f;
        [SerializeField] private float wetSeconds = 3f;
        [SerializeField] private Transform hostessNozzle;

        [Tooltip("Коллайдеры, которые не загораживают солнце (невидимое стекло в окне).")]
        [SerializeField] private Collider[] ignoreColliders;

        // Синхронизация пятна: от какой точки маршрута и с какого момента оно ползёт; облако.
        public readonly NetworkVariable<float> SegmentFrom = new();
        public readonly NetworkVariable<double> SegmentStart = new();
        public readonly NetworkVariable<double> CloudUntil = new();
        // Пшик: номер (меняется — новый пшик), куда и когда.
        public readonly NetworkVariable<int> SpraySerial = new();
        public readonly NetworkVariable<Vector3> SprayPoint = new();
        public readonly NetworkVariable<double> SprayImpact = new();
        public NetworkList<ulong> WetCats;

        // Сервер.
        private readonly Dictionary<ulong, float> _heat = new();
        private readonly Dictionary<ulong, double> _wetUntil = new();
        private readonly List<ulong> _lit = new();
        private double _nextCloud, _nextSpray;
        private bool _sprayPending;

        private float[] _cumulative;
        private float _loopLength;
        private static readonly RaycastHit[] Hits = new RaycastHit[12];
        private readonly HashSet<Collider> _ignored = new();

        public float Radius => radius;
        public Transform Nozzle => hostessNozzle;
        public double NowTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool Cloudy => NowTime < CloudUntil.Value;
        public bool FinalBoost => Phase == MiniGamePhase.Playing && TimeLeft > 0f && TimeLeft <= finalBoostSeconds;
        public bool IsWet(ulong clientId) => WetCats.Contains(clientId);
        protected override bool UsesScores => true;

        protected override void Awake()
        {
            base.Awake();
            WetCats = new NetworkList<ulong>();
            BuildPath();
            if (ignoreColliders != null)
                foreach (var c in ignoreColliders) if (c != null) _ignored.Add(c);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Грейся в солнечном пятне — 1 очко в секунду";
                var me = NetworkManager.Singleton != null ? CatController.FindByOwner(NetworkManager.Singleton.LocalClientId) : null;
                string boost = FinalBoost ? "Последние лучи — очки ×2! " : "";
                if (Cloudy) return boost + "Облако! Сейчас солнце выглянет в другом месте…";
                if (me != null && IsWet(me.OwnerClientId)) return boost + "Ты мокрый — 3 секунды не греешься";
                if (me != null && IsLit(me.transform.position)) return boost + "☀ Ты на солнышке! Не дай себя вытолкнуть";
                return boost + "Беги в пятно солнца и выталкивай оттуда соперников";
            }
        }

        // ───────────────────────── Маршрут и луч (у всех) ─────────────────────────

        private void BuildPath()
        {
            if (path == null || path.Length < 2) path = new[] { Vector3.zero, Vector3.forward };
            _cumulative = new float[path.Length + 1];
            for (int i = 0; i < path.Length; i++)
                _cumulative[i + 1] = _cumulative[i] + Vector3.Distance(path[i], path[(i + 1) % path.Length]);
            _loopLength = Mathf.Max(0.01f, _cumulative[path.Length]);
        }

        public float LoopLength => _loopLength;

        /// <summary>Точка маршрута на расстоянии distance от начала (по кругу).</summary>
        public Vector3 PathPoint(float distance)
        {
            float d = Mathf.Repeat(distance, _loopLength);
            for (int i = 0; i < path.Length; i++)
            {
                if (d > _cumulative[i + 1]) continue;
                float len = _cumulative[i + 1] - _cumulative[i];
                float t = len > 0.001f ? (d - _cumulative[i]) / len : 0f;
                // Плавно: на точках пятно чуть задерживается (сглаженная скорость).
                t = Mathf.SmoothStep(0f, 1f, t) * 0.35f + t * 0.65f;
                return Vector3.Lerp(path[i], path[(i + 1) % path.Length], t);
            }
            return path[0];
        }

        /// <summary>Куда сейчас светит пятно (центр на поверхности маршрута).</summary>
        public Vector3 SpotPoint(double time)
        {
            double since = time - SegmentStart.Value;
            if (since < 0) since = 0;
            return PathPoint(SegmentFrom.Value + (float)since * speed);
        }

        /// <summary>Точка в проёме окна, через которую проходит луч к пятну (смещается вслед за пятном).</summary>
        public Vector3 WindowPoint(Vector3 spot) =>
            new(Mathf.Clamp(spot.x * 0.45f, -windowHalfX, windowHalfX), windowY, windowZ);

        /// <summary>Луч: откуда (окно) и направление.</summary>
        public void Beam(double time, out Vector3 window, out Vector3 direction, out Vector3 spot)
        {
            spot = SpotPoint(time);
            window = WindowPoint(spot);
            direction = (spot - window).normalized;
        }

        /// <summary>Свет почти параллельный, как у солнца: «источник» далеко за окном.</summary>
        public const float ApexBack = 14f;
        /// <summary>Пятно — прямоугольник окна: полуширина и полувысота в долях игрового радиуса.</summary>
        public const float HalfWidthK = 1.15f, HalfHeightK = 0.8f;

        public Vector3 Apex(Vector3 window, Vector3 direction) => window - direction * ApexBack;

        /// <summary>Оси светового прямоугольника: вправо и вверх (поперёк луча).</summary>
        public static void BeamAxes(Vector3 direction, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, direction);
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            up = Vector3.Cross(direction, right).normalized;
        }

        /// <summary>Тангенсы половины ширины/высоты луча от «источника» (на пятне — прямоугольник окна).</summary>
        public void BeamTangents(Vector3 apex, Vector3 spot, out float tanW, out float tanH)
        {
            float d = Mathf.Max(0.5f, Vector3.Distance(apex, spot));
            tanW = radius * HalfWidthK / d;
            tanH = radius * HalfHeightK / d;
        }

        /// <summary>Освещена ли точка (кот): внутри светового «окна» и свет не загорожен мебелью — как у прожектора с маской окна.</summary>
        public bool IsLit(Vector3 catPosition)
        {
            if (Cloudy) return false;
            Beam(NowTime, out var window, out var direction, out var spot);
            Vector3 apex = Apex(window, direction);
            BeamAxes(direction, out var right, out var up);
            BeamTangents(apex, spot, out float tanW, out float tanH);
            Vector3 c = catPosition + Vector3.up * 0.3f;
            Vector3 v = c - apex;
            float along = Vector3.Dot(v, direction);
            if (along <= 0f) return false;
            if (Mathf.Abs(Vector3.Dot(v, right)) > tanW * along || Mathf.Abs(Vector3.Dot(v, up)) > tanH * along) return false;
            Vector3 toLight = (apex - c).normalized;
            return !EnvironmentRaycast(c, toLight, Vector3.Distance(c, window) + 0.5f, out _);
        }

        /// <summary>Луч по комнате: мебель и стены, без котов, хозяйки и стекла.</summary>
        public bool EnvironmentRaycast(Vector3 origin, Vector3 direction, float length, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance >= best || _ignored.Contains(hit.collider)) continue;
                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.GetComponent<CatController>() != null) continue;
                if (hit.collider.GetComponentInParent<CatController>() != null) continue;
                best = hit.distance;
                nearest = hit;
                found = true;
            }
            return found;
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            SegmentFrom.Value = 0f;
            SegmentStart.Value = Now + 3.5;
            CloudUntil.Value = 0;
        }

        protected override void OnGameStarted()
        {
            SegmentFrom.Value = 0f;
            SegmentStart.Value = Now;
            _nextCloud = Now + firstCloud;
            _nextSpray = Now + firstSpray;
        }

        protected override void OnGameTick()
        {
            float dt = Time.deltaTime;

            // Облако: пятно гаснет и выглядывает в новом месте маршрута.
            if (Now >= _nextCloud && !Cloudy)
            {
                CloudUntil.Value = Now + cloudDuration;
                float current = SegmentFrom.Value + (float)(Now - SegmentStart.Value) * speed;
                SegmentFrom.Value = current + Random.Range(_loopLength * 0.3f, _loopLength * 0.7f);
                SegmentStart.Value = Now + cloudDuration;
                _nextCloud = Now + cloudDuration + Random.Range(cloudInterval.x, cloudInterval.y);
                SunSoundRpc(false);
            }
            if (_cloudWasOn && !Cloudy) SunSoundRpc(true);
            _cloudWasOn = Cloudy;

            // Мокрые высыхают.
            for (int i = WetCats.Count - 1; i >= 0; i--)
            {
                ulong id = WetCats[i];
                if (!_wetUntil.TryGetValue(id, out double until) || Now >= until) WetCats.RemoveAt(i);
            }

            // Кто греется.
            _lit.Clear();
            if (!Cloudy)
            {
                foreach (ulong id in AlivePlayers)
                {
                    if (WetCats.Contains(id)) continue;
                    var cat = CatController.FindByOwner(id);
                    if (cat != null && IsLit(cat.transform.position)) _lit.Add(id);
                }
            }
            if (_lit.Count > 0)
            {
                float share = pointsPerSecond * (FinalBoost ? 2f : 1f) * dt / _lit.Count;
                foreach (ulong id in _lit)
                {
                    _heat.TryGetValue(id, out float heat);
                    heat += share;
                    if (heat >= 1f)
                    {
                        int whole = Mathf.FloorToInt(heat);
                        heat -= whole;
                        AddScore(id, whole);
                    }
                    _heat[id] = heat;
                }
            }

            // Хозяйка с пульверизатором.
            if (!_sprayPending && Now >= _nextSpray) StartSpray();
            if (_sprayPending && Now >= SprayImpact.Value) ResolveSpray();
        }

        private bool _cloudWasOn;

        private void StartSpray()
        {
            // Цель — лидер среди тех, кто сейчас греется; если никто — лидер вообще.
            ulong target = ulong.MaxValue;
            int best = -1;
            foreach (ulong id in AlivePlayers)
            {
                if (WetCats.Contains(id)) continue;
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                int score = GetScore(id) + (_lit.Contains(id) ? 1000 : 0) + Random.Range(0, 3);
                if (score > best) { best = score; target = id; }
            }
            var targetCat = target != ulong.MaxValue ? CatController.FindByOwner(target) : null;
            if (targetCat == null)
            {
                _nextSpray = Now + 2;
                return;
            }

            SprayPoint.Value = targetCat.transform.position;
            SprayImpact.Value = Now + sprayWarning;
            SpraySerial.Value++;
            _sprayPending = true;
        }

        private void ResolveSpray()
        {
            _sprayPending = false;
            _nextSpray = Now + Random.Range(sprayInterval.x, sprayInterval.y);
            Vector3 point = SprayPoint.Value;
            foreach (ulong id in AlivePlayers)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                Vector3 offset = cat.transform.position - point;
                if (Mathf.Abs(offset.y) > 1.6f) continue;
                offset.y = 0f;
                if (offset.magnitude > sprayRadius) continue;

                _wetUntil[id] = Now + wetSeconds;
                if (!WetCats.Contains(id)) WetCats.Add(id);
                Vector3 push = hostessNozzle != null ? cat.transform.position - hostessNozzle.position : -Vector3.forward;
                push.y = 0f;
                cat.ReceiveHitRpc(push.normalized * 2.2f + Vector3.up * 1.5f, true);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void SunSoundRpc(bool on)
        {
            if (on) AudioManager.Play("sun_on", 0.55f);
            else AudioManager.Play("wind", 0.45f);
        }
    }
}
