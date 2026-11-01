using System.Collections;
using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.Session
{
    /// <summary>
    /// Роль зрителя для выбывших. Живёт на префабе SessionManager, работает в любой мини-игре на выбывание.
    /// - Камера выбывшего переключается на живых котов: Tab — следующий, Shift+Tab — предыдущий.
    /// - R — кинуть тапок в того, за кем смотришь: тапок летит дугой и толкает кота (без оглушения).
    ///   Раз в throwCooldown секунд; в одного кота — не чаще раза в targetCooldown секунд (от всех зрителей вместе).
    /// Всё, что влияет на игру, решает сервер (проверка, что бросает выбывший, а цель жива). Клиенты рисуют полёт.
    /// </summary>
    public class SpectatorSystem : NetworkBehaviour
    {
        public static SpectatorSystem Instance { get; private set; }

        [SerializeField] private float throwCooldown = 8f;
        [SerializeField] private float targetCooldown = 2.5f;
        [SerializeField] private float flightTime = 0.6f;
        [SerializeField] private float hitForce = 3.2f;
        [SerializeField] private float hitLift = 2.2f;

        // Клиент.
        private bool _active;
        private ulong _watched = ulong.MaxValue;
        private float _nextThrow;
        private CatCamera _camera;
        private readonly List<ulong> _alive = new();

        // Сервер.
        private readonly Dictionary<ulong, float> _thrownAt = new();
        private readonly Dictionary<ulong, float> _targetHitAt = new();

        /// <summary>Свой кот выбыл и смотрит за живыми.</summary>
        public bool IsSpectating => _active;

        /// <summary>Секунд до следующего тапка (0 — можно).</summary>
        public float ThrowCooldownLeft => Mathf.Max(0f, _nextThrow - Time.time);

        /// <summary>Сообщение «в тебя кинули тапок» для живого игрока.</summary>
        public string VictimNotice { get; private set; } = "";
        public float VictimNoticeUntil { get; private set; }

        public string WatchedName
        {
            get
            {
                var player = NetworkPlayer.FindByClientId(_watched);
                return player != null ? player.DisplayName : "";
            }
        }

        public override void OnNetworkSpawn() => Instance = this;

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ───────────────────────── Клиент ─────────────────────────

        private void Update()
        {
            if (!IsSpawned) return;
            var nm = NetworkManager.Singleton;
            ulong me = nm.LocalClientId;
            var mode = MiniGameMode.Current;

            bool spectate = mode != null && mode.IsSpawned && mode.Phase == MiniGamePhase.Playing
                            && !mode.IsAlive(me) && mode.AliveCount > 0 && CatController.FindByOwner(me) != null;

            if (spectate != _active)
            {
                _active = spectate;
                CatVoice.Suppressed = _active; // R у зрителя — тапок, а не мяуканье
                if (_active)
                {
                    _watched = ulong.MaxValue;
                    Cycle(mode, 1);
                }
                else
                {
                    _watched = ulong.MaxValue;
                    var mine = CatController.FindByOwner(me);
                    Camera()?.SetTarget(mine != null ? mine.transform : null);
                }
            }
            if (!_active) return;

            // За кем смотрели — выбыл: сразу на следующего.
            if (!mode.IsAlive(_watched) || CatController.FindByOwner(_watched) == null) Cycle(mode, 1);

            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard == null || CatInput.Blocked) return;
            if (keyboard.tabKey.wasPressedThisFrame) Cycle(mode, keyboard.shiftKey.isPressed ? -1 : 1);
            if (keyboard.rKey.wasPressedThisFrame && Time.time >= _nextThrow && _watched != ulong.MaxValue)
            {
                _nextThrow = Time.time + throwCooldown;
                ThrowRpc(_watched);
            }
        }

        private CatCamera Camera()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                var main = UnityEngine.Camera.main;
                _camera = main != null ? main.GetComponent<CatCamera>() : null;
            }
            return _camera;
        }

        /// <summary>Следующий (или предыдущий) живой кот.</summary>
        private void Cycle(MiniGameMode mode, int step)
        {
            _alive.Clear();
            foreach (var id in mode.AlivePlayers)
                if (CatController.FindByOwner(id) != null) _alive.Add(id);
            if (_alive.Count == 0) return;
            _alive.Sort();

            int index = _alive.IndexOf(_watched);
            index = index < 0 ? 0 : ((index + step) % _alive.Count + _alive.Count) % _alive.Count;
            _watched = _alive[index];
            var cat = CatController.FindByOwner(_watched);
            if (cat != null) Camera()?.SetTarget(cat.transform);
        }

        // ───────────────────────── Сервер ─────────────────────────

        [Rpc(SendTo.Server)]
        private void ThrowRpc(ulong target, RpcParams rpcParams = default)
        {
            ulong thrower = rpcParams.Receive.SenderClientId;
            var mode = MiniGameMode.Current;
            if (mode == null || mode.Phase != MiniGamePhase.Playing) return;
            if (mode.IsAlive(thrower) || !mode.IsAlive(target)) return;
            if (_thrownAt.TryGetValue(thrower, out float last) && Time.time - last < throwCooldown - 0.5f) return;
            if (_targetHitAt.TryGetValue(target, out float hit) && Time.time - hit < targetCooldown) return;

            var cat = CatController.FindByOwner(target);
            if (cat == null) return;
            _thrownAt[thrower] = Time.time;
            SessionStats.Add(thrower, Stat.Slippers);
            _targetHitAt[target] = Time.time;

            // Тапок прилетает сбоку от камеры кота — направление случайное, чтобы не угадать.
            float angle = Random.Range(0f, 360f);
            Vector3 from = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 6f + Vector3.up * 4f;
            SlipperRpc(thrower, target, from);
            StartCoroutine(HitLater(target, -new Vector3(from.x, 0f, from.z).normalized));
        }

        private IEnumerator HitLater(ulong target, Vector3 direction)
        {
            yield return new WaitForSeconds(flightTime);
            var mode = MiniGameMode.Current;
            var cat = CatController.FindByOwner(target);
            if (cat == null || mode == null || !mode.IsAlive(target)) yield break;
            cat.ReceiveHitRpc(direction * hitForce + Vector3.up * hitLift, false);
        }

        // ───────────────────────── Полёт тапка (у всех) ─────────────────────────

        [Rpc(SendTo.Everyone)]
        private void SlipperRpc(ulong thrower, ulong target, Vector3 fromOffset)
        {
            var cat = CatController.FindByOwner(target);
            if (cat == null) return;
            StartCoroutine(Fly(cat.transform, fromOffset));
            AudioManager.PlayAt("push", cat.transform.position + fromOffset * 0.5f, 0.8f, 0.1f, 30f);

            if (target == NetworkManager.Singleton.LocalClientId)
            {
                var player = NetworkPlayer.FindByClientId(thrower);
                VictimNotice = $"{(player != null ? player.DisplayName : "Зритель")} кинул в тебя тапок!";
                VictimNoticeUntil = Time.time + 2.5f;
            }
        }

        private IEnumerator Fly(Transform target, Vector3 fromOffset)
        {
            var slipper = BuildSlipper();
            float t = 0f;
            Vector3 start = target.position + fromOffset;
            while (t < flightTime && target != null)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / flightTime);
                Vector3 end = target.position + Vector3.up * 0.4f;
                Vector3 p = Vector3.Lerp(start, end, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 1.5f);
                slipper.transform.position = p;
                slipper.transform.rotation = Quaternion.Euler(u * 900f, u * 400f, 0f);
                yield return null;
            }
            if (target != null) AudioManager.PlayAt("hit", target.position, 0.9f, 0.1f, 30f);
            // Отскочил и упал.
            Vector3 bounce = new(Random.Range(-1f, 1f), 2.5f, Random.Range(-1f, 1f));
            float fall = 0f;
            while (fall < 0.6f && slipper != null)
            {
                fall += Time.deltaTime;
                bounce += Vector3.down * 12f * Time.deltaTime;
                slipper.transform.position += bounce * Time.deltaTime;
                slipper.transform.Rotate(400f * Time.deltaTime, 0f, 200f * Time.deltaTime);
                yield return null;
            }
            if (slipper != null) Destroy(slipper);
        }

        private static Material _sole, _upper;

        /// <summary>Тапок из двух коробочек: подошва и клетчатый верх.</summary>
        private static GameObject BuildSlipper()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (_sole == null) _sole = new Material(shader) { color = new Color(0.25f, 0.18f, 0.14f) };
            if (_upper == null) _upper = new Material(shader) { color = new Color(0.75f, 0.22f, 0.25f) };

            var root = new GameObject("Slipper");
            var sole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(sole.GetComponent<Collider>());
            sole.transform.SetParent(root.transform, false);
            sole.transform.localScale = new Vector3(0.22f, 0.05f, 0.55f);
            sole.GetComponent<Renderer>().sharedMaterial = _sole;
            var upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(upper.GetComponent<Collider>());
            upper.transform.SetParent(root.transform, false);
            upper.transform.localPosition = new Vector3(0f, 0.07f, 0.1f);
            upper.transform.localScale = new Vector3(0.23f, 0.1f, 0.3f);
            upper.GetComponent<Renderer>().sharedMaterial = _upper;
            return root;
        }
    }
}
