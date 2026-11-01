using System;
using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Gameplay;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Maoshi.MiniGames.PondFishing
{
    /// <summary>Рыба в пруду: путь целиком задан семенем и временем появления — у всех клиентов одинаковый.</summary>
    public struct PondFish : INetworkSerializable, IEquatable<PondFish>
    {
        public int Id;
        public int Seed;
        public double SpawnTime;
        public float Life;
        /// <summary>0 — мелкая, 1 — средняя, 2 — крупная, 3 — золотая.</summary>
        public byte Size;
        public byte Hits;
        public bool Caught;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Seed);
            serializer.SerializeValue(ref SpawnTime);
            serializer.SerializeValue(ref Life);
            serializer.SerializeValue(ref Size);
            serializer.SerializeValue(ref Hits);
            serializer.SerializeValue(ref Caught);
        }

        public bool Equals(PondFish other) => Id == other.Id && Hits == other.Hits && Caught == other.Caught;
    }

    /// <summary>
    /// «Рыбалка лапой в пруду». Коты стоят на кочках, под водой плавают тени рыб.
    /// Удар лапой (F / ЛКМ) ровно когда тень под лапой — поймал: мелкая +1, средняя +2, крупная +4 (три удара подряд),
    /// золотая +6 (быстрая, два удара). Между ударами по одной рыбе — не дольше 1.3 с, иначе вырвалась.
    /// Кочки по очереди трясутся и уходят под воду; соседи толкаются. Упал в воду — 2.5 с мокнешь, потом на свою кочку.
    /// Сервер решает: рыб, попадания, очки, кто в воде. Пути рыб клиенты считают сами (по семени).
    /// </summary>
    public class FishingMode : MiniGameMode
    {
        private static readonly int[] HitsNeeded = { 1, 1, 3, 2 };
        private static readonly int[] Points = { 1, 2, 4, 6 };
        private static readonly float[] Speeds = { 1.5f, 1.15f, 0.75f, 2.3f };
        private static readonly float[] ShadowSizes = { 0.45f, 0.7f, 1.15f, 0.6f };

        [Header("Пруд")]
        [SerializeField] private Vector3 pondCenter;
        [SerializeField] private float innerRadius = 1.2f;
        [SerializeField] private float outerRadius = 5.4f;
        [SerializeField] private float waterY = -0.15f;
        [SerializeField] private int fishCount = 7;
        [SerializeField] private float reach = 1.05f;
        [SerializeField] private float hitRadius = 0.6f;
        [SerializeField] private float struggleWindow = 1.3f;
        [SerializeField] private float wetSeconds = 1f;

        [Header("Картинка")]
        [SerializeField] private Material shadowMaterial;
        [SerializeField] private Material goldMaterial;
        [SerializeField] private GameObject fishModel;
        [SerializeField] private Material ringMaterial;
        [SerializeField] private Material ringActiveMaterial;

        public readonly NetworkVariable<double> GameStart = new(-1);
        public NetworkList<PondFish> Fish;

        // Сервер.
        private int _nextId;
        private readonly Dictionary<int, double> _lastHit = new();
        private readonly Dictionary<ulong, double> _wetUntil = new();
        private readonly Dictionary<ulong, double> _lastSwipe = new();
        private float _nextLocalSwipe;

        // Клиент.
        private readonly Dictionary<int, Transform> _shadows = new();
        private Transform _ring;
        private Renderer _ringRenderer;

        protected override bool UsesScores => true;

        protected override void Awake()
        {
            base.Awake();
            Fish = new NetworkList<PondFish>();
            if (fishModel != null) fishModel.SetActive(false);
        }

        // ───────────────────────── Путь рыбы ─────────────────────────

        /// <summary>Где рыба в момент time (общий для сервера и клиентов расчёт).</summary>
        public Vector3 FishPosition(in PondFish fish, double time)
        {
            var r = new System.Random(fish.Seed);
            float p1 = (float)r.NextDouble() * 6.283f;
            float w1 = 0.35f + (float)r.NextDouble() * 0.45f;
            float theta0 = (float)r.NextDouble() * 6.283f;
            float dir = r.NextDouble() < 0.5 ? -1f : 1f;
            float t = (float)(time - fish.SpawnTime);
            float radius = Mathf.Lerp(innerRadius, outerRadius, 0.5f + 0.5f * Mathf.Sin(w1 * t + p1));
            float avg = (innerRadius + outerRadius) * 0.5f;
            float theta = theta0 + dir * Speeds[fish.Size] / avg * t + 0.25f * Mathf.Sin(t * 1.7f + p1);
            return pondCenter + new Vector3(Mathf.Cos(theta) * radius, 0f, Mathf.Sin(theta) * radius);
        }

        private Vector3 FishHeading(in PondFish fish, double time)
        {
            Vector3 a = FishPosition(fish, time);
            Vector3 b = FishPosition(fish, time + 0.1);
            Vector3 d = b - a;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        private bool Alive(in PondFish fish, double time) => !fish.Caught && time >= fish.SpawnTime && time < fish.SpawnTime + fish.Life;

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            Fish.Clear();
            _wetUntil.Clear();
        }

        protected override void OnGameStarted()
        {
            GameStart.Value = Now;
            for (int i = 0; i < fishCount; i++) SpawnFish(Now - Random.Range(0f, 5f));
        }

        private void SpawnFish(double at)
        {
            float roll = Random.value;
            byte size = roll < 0.42f ? (byte)0 : roll < 0.75f ? (byte)1 : roll < 0.93f ? (byte)2 : (byte)3;
            Fish.Add(new PondFish
            {
                Id = _nextId++,
                Seed = Random.Range(1, int.MaxValue),
                SpawnTime = at,
                Life = Random.Range(12f, 20f),
                Size = size,
            });
        }

        protected override void OnGameTick()
        {
            // Уплывшие и пойманные — заменить новыми.
            for (int i = Fish.Count - 1; i >= 0; i--)
            {
                var f = Fish[i];
                if (f.Caught || Now >= f.SpawnTime + f.Life)
                {
                    _lastHit.Remove(f.Id);
                    Fish.RemoveAt(i);
                    SpawnFish(Now + Random.Range(0.3f, 1.5f));
                    continue;
                }
                // Не добил крупную вовремя — вырвалась (счётчик ударов сбрасывается).
                if (f.Hits > 0 && _lastHit.TryGetValue(f.Id, out double last) && Now - last > struggleWindow)
                {
                    f.Hits = 0;
                    Fish[i] = f;
                    _lastHit.Remove(f.Id);
                }
            }

            // Кто в воде — плюх, а через пару секунд обратно на свою кочку.
            foreach (var id in Participants)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                bool inWater = cat.transform.position.y < waterY - 0.3f;
                if (_wetUntil.TryGetValue(id, out double until))
                {
                    if (Now >= until)
                    {
                        _wetUntil.Remove(id);
                        if (CatSpawner.Current != null) CatSpawner.Current.ReturnToSpawn(cat);
                    }
                    continue;
                }
                if (inWater)
                {
                    _wetUntil[id] = Now + wetSeconds;
                    SplashRpc(id, cat.transform.position);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SwipeRpc(RpcParams rpcParams = default)
        {
            ulong id = rpcParams.Receive.SenderClientId;
            if (Phase != MiniGamePhase.Playing || _wetUntil.ContainsKey(id)) return;
            // Не чаще замаха лапой: долбить кнопку бесполезно.
            if (_lastSwipe.TryGetValue(id, out double lastSwipe) && Now - lastSwipe < 0.35) return;
            _lastSwipe[id] = Now;
            var cat = CatController.FindByOwner(id);
            if (cat == null || cat.State.Value != CatState.Normal) return;

            Vector3 paw = PawPoint(cat.transform);
            int best = -1;
            float bestDistance = hitRadius + 0.25f; // запас на задержку сети
            for (int i = 0; i < Fish.Count; i++)
            {
                var f = Fish[i];
                if (!Alive(f, Now)) continue;
                Vector3 p = FishPosition(f, Now);
                float d = new Vector2(p.x - paw.x, p.z - paw.z).magnitude - ShadowSizes[f.Size] * 0.3f;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best < 0) return;

            var fish = Fish[best];
            fish.Hits++;
            _lastHit[fish.Id] = Now;
            if (fish.Hits >= HitsNeeded[fish.Size])
            {
                fish.Caught = true;
                Fish[best] = fish;
                AddScore(id, Points[fish.Size]);
                CatchRpc(id, FishPosition(fish, Now), fish.Size);
            }
            else
            {
                Fish[best] = fish;
                StruggleRpc(id, FishPosition(fish, Now), fish.Hits, HitsNeeded[fish.Size]);
            }
        }

        private Vector3 PawPoint(Transform cat)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cat.forward, Vector3.up).normalized;
            return cat.position + forward * reach;
        }

        // ───────────────────────── Сеть: всем ─────────────────────────

        [Rpc(SendTo.Everyone)]
        private void CatchRpc(ulong clientId, Vector3 at, byte size)
        {
            AudioManager.PlayAt("splash", at, 0.8f, 0.1f, 30f);
            AudioManager.PlayAt(size >= 2 ? "fish_big" : "fish", at, 0.9f, 0.02f, 40f);
            var cat = CatController.FindByOwner(clientId);
            if (fishModel != null && cat != null) FishJump.Launch(fishModel, size == 3 ? goldMaterial : null, at + Vector3.up * waterY, cat.transform, 0.4f + size * 0.18f);
            if (NetworkManager != null && clientId == NetworkManager.LocalClientId)
            {
                _toast = size switch { 0 => "Мелкая! +1", 1 => "Рыбёшка! +2", 2 => "КРУПНАЯ! +4", _ => "ЗОЛОТАЯ! +6" };
                _toastUntil = Time.time + 1.6f;
            }
        }

        [Rpc(SendTo.Everyone)]
        private void StruggleRpc(ulong clientId, Vector3 at, int hits, int needed)
        {
            AudioManager.PlayAt("splash", at, 0.5f, 0.15f, 25f);
            if (NetworkManager != null && clientId == NetworkManager.LocalClientId)
            {
                _toast = $"Держи! Ещё {needed - hits} удар{(needed - hits == 1 ? "" : "а")} — быстрее!";
                _toastUntil = Time.time + 1.3f;
            }
        }

        [Rpc(SendTo.Everyone)]
        private void SplashRpc(ulong clientId, Vector3 at)
        {
            AudioManager.PlayAt("splash", new Vector3(at.x, waterY, at.z), 1f, 0.05f, 40f);
            if (NetworkManager != null && clientId == NetworkManager.LocalClientId)
            {
                AudioManager.Play("ouch", 0.7f);
                _toast = "Плюх! Сушись пару секунд…";
                _toastUntil = Time.time + wetSeconds;
            }
        }

        private string _toast = "";
        private float _toastUntil = -1f;

        // ───────────────────────── Клиент ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned || NetworkManager == null) return;
            double now = NetworkManager.ServerTime.Time;

            if (Phase == MiniGamePhase.Playing && !CatInput.Blocked && !CatInput.UIWantsCursor)
            {
                var keyboard = Maoshi.GameKeyboard.Current;
                var mouse = Mouse.current;
                bool swipe = (keyboard != null && keyboard.fKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
                if (swipe && Time.time >= _nextLocalSwipe)
                {
                    _nextLocalSwipe = Time.time + 0.45f;
                    SwipeRpc();
                }
            }

            UpdateShadows(now);
            UpdateRing(now);
        }

        private void UpdateShadows(double now)
        {
            var alive = new HashSet<int>();
            foreach (var f in Fish)
            {
                if (f.Caught || now < f.SpawnTime - 0.5 || now > f.SpawnTime + f.Life) continue;
                alive.Add(f.Id);
                if (!_shadows.TryGetValue(f.Id, out var shadow) || shadow == null)
                {
                    shadow = MakeShadow(f.Size);
                    _shadows[f.Id] = shadow;
                }
                // Появление и исчезновение — плавно (рыба поднимается из глубины и уходит вниз).
                float age = (float)(now - f.SpawnTime);
                float left = (float)(f.SpawnTime + f.Life - now);
                float fade = Mathf.Clamp01(Mathf.Min(age, left) / 1.2f);
                Vector3 p = FishPosition(f, now);
                Vector3 heading = FishHeading(f, now);
                float wiggle = Mathf.Sin((float)now * (8f - f.Size * 1.5f) + f.Seed % 10) * 12f;
                if (f.Hits > 0) wiggle *= 3f; // бьётся
                shadow.SetPositionAndRotation(new Vector3(p.x, waterY + 0.02f, p.z), Quaternion.LookRotation(heading) * Quaternion.Euler(0f, wiggle, 0f));
                float s = ShadowSizes[f.Size] * Mathf.Lerp(0.3f, 1f, fade);
                shadow.localScale = new Vector3(s * 0.45f, 0.01f, s);
            }
            var stale = new List<int>();
            foreach (var pair in _shadows) if (!alive.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var id in stale)
            {
                if (_shadows[id] != null) Destroy(_shadows[id].gameObject);
                _shadows.Remove(id);
            }
        }

        private Transform MakeShadow(byte size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FishShadow";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = size == 3 && goldMaterial != null ? goldMaterial : shadowMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Хвостик.
            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(tail.GetComponent<Collider>());
            tail.transform.SetParent(go.transform, false);
            tail.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            tail.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            tail.transform.localScale = new Vector3(0.7f, 1f, 0.3f);
            tail.GetComponent<Renderer>().sharedMaterial = r.sharedMaterial;
            tail.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>Кружок под лапой своего кота: загорается, когда тень рыбы в зоне удара.</summary>
        private void UpdateRing(double now)
        {
            var cat = CatController.FindByOwner(NetworkManager.LocalClientId);
            bool show = cat != null && Phase == MiniGamePhase.Playing;
            if (_ring == null && show)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "PawRing";
                Destroy(go.GetComponent<Collider>());
                _ring = go.transform;
                _ringRenderer = go.GetComponent<Renderer>();
                _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_ring == null) return;
            _ring.gameObject.SetActive(show);
            if (!show) return;

            Vector3 paw = PawPoint(cat.transform);
            bool fishHere = false;
            foreach (var f in Fish)
            {
                if (!Alive(f, now)) continue;
                Vector3 p = FishPosition(f, now);
                if (new Vector2(p.x - paw.x, p.z - paw.z).magnitude - ShadowSizes[f.Size] * 0.3f < hitRadius) { fishHere = true; break; }
            }
            _ring.position = new Vector3(paw.x, waterY + 0.04f, paw.z);
            float pulse = fishHere ? 1f + Mathf.Sin(Time.time * 18f) * 0.08f : 1f;
            _ring.localScale = new Vector3(hitRadius * 2f * pulse, 0.01f, hitRadius * 2f * pulse);
            _ringRenderer.sharedMaterial = fishHere ? ringActiveMaterial : ringMaterial;
        }

        public override void OnNetworkDespawn()
        {
            foreach (var s in _shadows.Values) if (s != null) Destroy(s.gameObject);
            _shadows.Clear();
            if (_ring != null) Destroy(_ring.gameObject);
            base.OnNetworkDespawn();
        }

        public override string StatusText
        {
            get
            {
                if (Time.time < _toastUntil) return _toast;
                return Phase == MiniGamePhase.Playing
                    ? "Бей лапой (F / ЛКМ), когда тень рыбы в кружке! Крупную — три раза подряд. Кочки уходят под воду — прыгай!"
                    : "Лови рыбу лапой с кочек. Крупная дороже, но бьётся. Не упади в воду!";
            }
        }
    }
}
