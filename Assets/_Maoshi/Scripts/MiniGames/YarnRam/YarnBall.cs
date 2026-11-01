using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.YarnRam
{
    /// <summary>
    /// Гигантский клубок ниток. Принадлежит серверу (NetworkTransform + NetworkRigidbody).
    /// - Толчок лапой (через CatController.PushRpc) сильно пинает клубок в сторону от кота.
    /// - Катящийся клубок, налетев на кота, сбивает его — тем сильнее, чем быстрее катится
    ///   (скорость берём до столкновения — иначе удар почти никогда не засчитывался).
    /// - Закатившись в корзинку, приносит её хозяину Points очков.
    /// Удар отправляется владельцу кота через ReceiveHitRpc (физику кота считает его владелец).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class YarnBall : NetworkBehaviour, IPushReceiver
    {
        [Header("Пинок лапой")]
        [Tooltip("Скорость, которую клубок получает от пинка (м/с).")]
        [SerializeField] private float kickSpeed = 7.5f;
        [SerializeField] private float kickUp = 1.2f;
        [SerializeField] private float maxSpeed = 11f;

        [Header("Таран")]
        [Tooltip("Медленнее этого клубок котов не сбивает, а просто толкает.")]
        [SerializeField] private float minRamSpeed = 2.5f;
        [Tooltip("Сила удара = скорость клубка × это число (потом её ограничит кот).")]
        [SerializeField] private float ramMultiplier = 0.9f;
        [SerializeField] private float ramUp = 2.5f;
        [Tooltip("Повторно того же кота этот клубок может сбить не раньше чем через столько секунд.")]
        [SerializeField] private float sameCatCooldown = 0.7f;

        [Header("Корзинка")]
        [Tooltip("Сколько очков приносит клубок, закатившись в корзинку (золотой — больше).")]
        [SerializeField] private int points = 1;

        public int Points => points;

        /// <summary>Кто пнул клубок последним (для подсказки «кто кого сбил»).</summary>
        public ulong LastKicker { get; private set; } = ulong.MaxValue;

        private Rigidbody _body;
        private readonly Dictionary<ulong, float> _lastHit = new();

        public Rigidbody Body => _body != null ? _body : _body = GetComponent<Rigidbody>();

        private Vector3 _prevVelocity;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        // Скорость до столкновения: в OnCollisionEnter клубок уже замедлился от удара о кота.
        private void FixedUpdate()
        {
            if (IsServer && _body != null && !_body.isKinematic) _prevVelocity = _body.linearVelocity;
        }

        // ───────────────────────── Пинок ─────────────────────────

        public void OnPushedByCat(CatController pusher, Vector3 direction)
        {
            if (!IsServer || Body.isKinematic) return;

            Vector3 velocity = Body.linearVelocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            // Пинок задаёт новое направление, но немного сохраняет прежнюю скорость — можно «подкручивать».
            flat = direction * kickSpeed + flat * 0.25f;
            flat = Vector3.ClampMagnitude(flat, maxSpeed);
            Body.linearVelocity = new Vector3(flat.x, Mathf.Max(velocity.y, kickUp), flat.z);
            LastKicker = pusher.OwnerClientId;
        }

        // ───────────────────────── Таран ─────────────────────────

        private void OnCollisionEnter(Collision collision) => TryRam(collision);

        private void OnCollisionStay(Collision collision) => TryRam(collision);

        private void TryRam(Collision collision)
        {
            if (!IsServer || !IsSpawned || Body.isKinematic) return;

            var cat = collision.rigidbody != null ? collision.rigidbody.GetComponent<CatController>() : null;
            if (cat == null || !cat.IsSpawned) return;

            Vector3 velocity = Body.linearVelocity;
            if (_prevVelocity.sqrMagnitude > velocity.sqrMagnitude) velocity = _prevVelocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            float speed = flat.magnitude;
            if (speed < minRamSpeed) return;

            ulong id = cat.OwnerClientId;
            if (_lastHit.TryGetValue(id, out float last) && Time.time - last < sameCatCooldown) return;
            _lastHit[id] = Time.time;

            // Бьём в сторону движения клубка, чуть отклоняя от центра кота — так «сносит» вбок.
            Vector3 away = Vector3.ProjectOnPlane(cat.transform.position - transform.position, Vector3.up).normalized;
            Vector3 direction = (flat.normalized * 0.7f + away * 0.3f).normalized;
            Vector3 impulse = direction * (speed * ramMultiplier) + Vector3.up * ramUp;
            cat.ReceiveHitRpc(impulse, speed > 6.5f); // быстрый клубок валит с лап

            // Клубок теряет часть скорости, но продолжает катиться.
            Body.linearVelocity = new Vector3(velocity.x * 0.6f, Body.linearVelocity.y, velocity.z * 0.6f);
            _prevVelocity = Body.linearVelocity;
            RamRpc(cat.transform.position);
        }

        [Rpc(SendTo.Everyone)]
        private void RamRpc(Vector3 at) => Maoshi.Audio.AudioManager.PlayAt("hit", at, 1f, 0.08f, 30f);

        /// <summary>Сервер: поставить клубок в точку и обнулить скорость.</summary>
        public void ResetTo(Vector3 position)
        {
            if (!IsServer) return;
            // Скорость можно задавать только подвижному телу (спрятанные клубки — кинематические).
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            LastKicker = ulong.MaxValue;
            _lastHit.Clear();
            _prevVelocity = Vector3.zero;

            if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform netTransform))
                netTransform.Teleport(position, Quaternion.identity, transform.localScale);
            else
                transform.SetPositionAndRotation(position, Quaternion.identity);
            Body.position = position;
        }
    }
}
