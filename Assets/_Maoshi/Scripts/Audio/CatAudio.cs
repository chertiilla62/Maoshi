using Maoshi.Cat;
using UnityEngine;

namespace Maoshi.Audio
{
    /// <summary>
    /// Звуки кота у всех игроков: шаги, прыжок, приземление, толчок, удар, «ай!» при падении, мяуканье.
    /// Шаги и прыжки считаются по движению модели (работает и для чужих котов, у которых нет своей физики).
    /// </summary>
    public class CatAudio : MonoBehaviour
    {
        [SerializeField] private float stepVolume = 0.3f;

        private CatController _cat;
        private Collider[] _ownColliders;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        private Vector3 _lastPosition;
        private float _verticalSpeed;
        private bool _grounded = true;
        private float _airTime;
        private float _minAirSpeed;
        private float _stepTimer;
        private float _lastJumpSound;

        private void Awake()
        {
            _cat = GetComponent<CatController>();
            _ownColliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            if (_cat == null) return;
            _cat.PushPerformed += OnPush;
            _cat.HitReceived += OnHit;
            _cat.State.OnValueChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (_cat == null) return;
            _cat.PushPerformed -= OnPush;
            _cat.HitReceived -= OnHit;
            _cat.State.OnValueChanged -= OnStateChanged;
        }

        private void OnPush() => AudioManager.PlayAt("push", transform.position + Vector3.up * 0.3f, 0.7f);

        private void OnHit()
        {
            AudioManager.PlayAt("hit", transform.position + Vector3.up * 0.3f, 0.85f);
            if (Random.value < 0.35f) AudioManager.PlayAt("ouch", transform.position + Vector3.up * 0.4f, 0.7f, 0.1f);
        }

        private void OnStateChanged(CatState previous, CatState current)
        {
            if (current == CatState.Knocked) AudioManager.PlayAt("ouch", transform.position + Vector3.up * 0.4f, 0.9f, 0.1f);
        }

        /// <summary>Мяукнуть (вызывает CatVoice у всех игроков).</summary>
        public void Meow(int kind)
        {
            Vector3 head = transform.position + Vector3.up * 0.45f;
            switch (kind)
            {
                case 1: AudioManager.PlayAt("hiss", head, 0.9f, 0.05f, 35f); break;
                case 2: AudioManager.PlayAt("mrrp", head, 0.9f, 0.1f, 35f); break;
                default: AudioManager.PlayAt("meow", head, 1f, 0.12f, 40f); break;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            Vector3 velocity = (position - _lastPosition) / dt;
            _lastPosition = position;
            // Телепорт (возврат на карту) — не звук.
            if (velocity.sqrMagnitude > 40f * 40f) return;

            _verticalSpeed = Mathf.Lerp(_verticalSpeed, velocity.y, 1f - Mathf.Exp(-20f * dt));
            bool grounded = CheckGround(position);

            if (!grounded)
            {
                _airTime += dt;
                _minAirSpeed = Mathf.Min(_minAirSpeed, _verticalSpeed);
                if (_grounded && _verticalSpeed > 2.5f && Time.time - _lastJumpSound > 0.3f)
                {
                    _lastJumpSound = Time.time;
                    AudioManager.PlayAt("jump", position, 0.5f);
                }
            }
            else
            {
                if (!_grounded && _airTime > 0.25f && _minAirSpeed < -3f)
                    AudioManager.PlayAt("land", position, Mathf.Clamp01(-_minAirSpeed / 9f) * 0.8f + 0.2f);
                _airTime = 0f;
                _minAirSpeed = 0f;

                float speed = new Vector2(velocity.x, velocity.z).magnitude;
                if (speed > 0.8f && (_cat == null || _cat.State.Value == CatState.Normal))
                {
                    _stepTimer -= dt;
                    if (_stepTimer <= 0f)
                    {
                        _stepTimer = Mathf.Clamp(0.42f - speed * 0.035f, 0.17f, 0.42f);
                        bool sneaking = _cat != null && _cat.Crouching.Value;
                        AudioManager.PlayAt("step", position, stepVolume * (sneaking ? 0.4f : 1f), 0.12f, 14f);
                    }
                }
                else _stepTimer = 0.05f;
            }
            _grounded = grounded;
        }

        private bool CheckGround(Vector3 position)
        {
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 0.3f, Vector3.down, _hits, 0.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                bool own = false;
                foreach (var c in _ownColliders)
                    if (_hits[i].collider == c) { own = true; break; }
                if (!own) return true;
            }
            return false;
        }
    }
}
