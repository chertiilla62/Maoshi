using Maoshi.Audio;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Maoshi.MiniGames.WindowHeist
{
    /// <summary>
    /// Пёс в квартире. Ходит сервер (NavMeshAgent только на сервере, позиция — NetworkTransform),
    /// клиенты рисуют позу процедурно: спит (дышит, храпит, «Zzz»), ворочается и рычит (шум ≥ 70%),
    /// просыпается с лаем, бежит (лапы, хвост), возвращается и укладывается.
    /// </summary>
    public class Dog : NetworkBehaviour
    {
        public enum DogState : byte { Sleeping, Waking, Chasing, Returning }

        [Header("Части модели (собирает этап 26)")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Transform tail;
        [SerializeField] private Transform[] legs = new Transform[4];
        [SerializeField] private Transform earL;
        [SerializeField] private Transform earR;
        [SerializeField] private TextMesh sign;

        [Header("Бег")]
        [SerializeField] private float chaseSpeed = 5.3f;
        [SerializeField] private float returnSpeed = 2.6f;

        public readonly NetworkVariable<DogState> State = new(DogState.Sleeping);
        /// <summary>0..1 — шум из WindowMode (для «ворочается»).</summary>
        public float Restless { get; set; }

        public Vector3 BedPosition { get; private set; }
        public Quaternion BedRotation { get; private set; }

        private NavMeshAgent _agent;
        private Vector3 _lastPosition;
        private float _speed;
        private float _phase;
        private float _stand;
        private float _nextSnore;
        private float _nextGrowl;
        private Quaternion[] _legRest;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            BedPosition = transform.position;
            BedRotation = transform.rotation;
            _legRest = new Quaternion[legs.Length];
            for (int i = 0; i < legs.Length; i++) if (legs[i] != null) _legRest[i] = legs[i].localRotation;
        }

        public override void OnNetworkSpawn()
        {
            if (_agent != null) _agent.enabled = IsServer;
            State.OnValueChanged += OnStateChanged;
        }

        public override void OnNetworkDespawn() => State.OnValueChanged -= OnStateChanged;

        // ───────────────────────── Сервер ─────────────────────────

        public void ServerWake() => State.Value = DogState.Waking;

        public void ServerChase(Vector3 target)
        {
            State.Value = DogState.Chasing;
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.speed = chaseSpeed;
            _agent.SetDestination(target);
        }

        public void ServerReturn()
        {
            State.Value = DogState.Returning;
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.speed = returnSpeed;
            _agent.SetDestination(BedPosition);
        }

        public bool ServerAtBed => (transform.position - BedPosition).sqrMagnitude < 0.3f * 0.3f ||
                                   (_agent != null && _agent.isOnNavMesh && !_agent.pathPending && _agent.remainingDistance < 0.35f);

        public void ServerSleep()
        {
            State.Value = DogState.Sleeping;
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
            transform.rotation = BedRotation;
        }

        public void ServerStop()
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
        }

        // ───────────────────────── Картинка ─────────────────────────

        private void OnStateChanged(DogState previous, DogState next)
        {
            if (next == DogState.Waking) AudioManager.PlayAt("bark", transform.position + Vector3.up * 0.6f, 1f, 0.05f, 45f);
        }

        [Rpc(SendTo.Everyone)]
        public void BarkRpc() => AudioManager.PlayAt("bark", transform.position + Vector3.up * 0.6f, 1f, 0.08f, 45f);

        private void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 position = transform.position;
            float frame = new Vector3(position.x - _lastPosition.x, 0f, position.z - _lastPosition.z).magnitude / dt;
            _lastPosition = position;
            _speed = Mathf.Lerp(_speed, Mathf.Min(frame, 10f), 1f - Mathf.Exp(-8f * dt));

            var state = State.Value;
            bool sleeping = state == DogState.Sleeping;
            _stand = Mathf.MoveTowards(_stand, sleeping ? 0f : 1f, dt * (sleeping ? 1.2f : 3.5f));

            // Корпус: лёжа — ниже, дышит; стоя — подпрыгивает на бегу.
            float breathe = Mathf.Sin(Time.time * (sleeping ? 1.6f : 4f)) * (sleeping ? 0.03f : 0.01f);
            _phase += dt * Mathf.Lerp(0f, 14f, Mathf.Clamp01(_speed / 4f));
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * Mathf.Clamp01(_speed / 3f);
            if (body != null)
            {
                body.localPosition = new Vector3(0f, Mathf.Lerp(-0.28f, 0f, _stand) + bob, 0f);
                body.localScale = new Vector3(1f + breathe, 1f + breathe * 0.6f, 1f);
            }

            // Лапы: лёжа вытянуты вперёд/назад, на бегу — мах.
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                bool front = i < 2;
                float lie = front ? -80f : 75f;
                float swing = Mathf.Sin(_phase + (i == 0 || i == 3 ? 0f : Mathf.PI)) * 40f * Mathf.Clamp01(_speed / 3f);
                legs[i].localRotation = _legRest[i] * Quaternion.Euler(Mathf.Lerp(lie, swing, _stand), 0f, 0f);
            }

            // Голова: спит — лежит на лапах; ворочается — приподнимает; бежит — вперёд.
            float restless = sleeping ? Mathf.Clamp01((Restless - 0.6f) / 0.4f) : 0f;
            if (head != null)
            {
                float pitch = sleeping ? Mathf.Lerp(25f, -10f, restless) + Mathf.Sin(Time.time * 7f) * 4f * restless : -5f + Mathf.Sin(_phase * 2f) * 4f;
                float yaw = sleeping ? Mathf.Sin(Time.time * 0.9f) * 20f * restless : 0f;
                head.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            float earFlop = state == DogState.Waking ? -40f : Mathf.Sin(_phase) * 15f * _stand;
            if (earL != null) earL.localRotation = Quaternion.Euler(0f, 0f, 20f + earFlop);
            if (earR != null) earR.localRotation = Quaternion.Euler(0f, 0f, -20f - earFlop);
            if (tail != null)
            {
                float wag = state == DogState.Chasing || state == DogState.Waking ? Mathf.Sin(Time.time * 18f) * 35f : Mathf.Sin(Time.time * 1.5f) * 6f;
                tail.localRotation = Quaternion.Euler(Mathf.Lerp(-10f, 35f, _stand), wag, 0f);
            }

            // Знак над головой и звуки.
            if (sign != null)
            {
                string text = state switch
                {
                    DogState.Sleeping => restless > 0.05f ? "?!" : (Time.time % 3f < 2f ? "Z z z" : "Z z"),
                    DogState.Waking => "ГАВ!",
                    DogState.Chasing => "!!",
                    _ => "",
                };
                if (sign.text != text) sign.text = text;
                sign.color = state == DogState.Sleeping && restless < 0.05f ? new Color(0.75f, 0.8f, 1f) : new Color(1f, 0.35f, 0.25f);
                sign.transform.position = transform.position + Vector3.up * (1.45f + Mathf.Sin(Time.time * 2f) * 0.05f);
                var camera = Camera.main;
                if (camera != null) sign.transform.rotation = Quaternion.LookRotation(sign.transform.position - camera.transform.position);
            }

            if (sleeping && Time.time >= _nextSnore)
            {
                _nextSnore = Time.time + 2.6f;
                AudioManager.PlayAt("snore", transform.position + Vector3.up * 0.4f, 0.55f, 0.05f, 14f);
            }
            if (sleeping && restless > 0.3f && Time.time >= _nextGrowl)
            {
                _nextGrowl = Time.time + 2.2f;
                AudioManager.PlayAt("growl", transform.position + Vector3.up * 0.4f, 0.8f, 0.05f, 25f);
            }
        }
    }
}
