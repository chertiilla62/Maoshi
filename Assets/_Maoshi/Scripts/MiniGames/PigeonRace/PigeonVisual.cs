using UnityEngine;

namespace Maoshi.MiniGames.PigeonRace
{
    /// <summary>
    /// Голубь у каждого игрока: плавно догоняет серверную позицию, в полёте машет крыльями,
    /// на насесте сложил крылья и клюёт. Только визуал — правилами управляет PigeonRaceMode.
    /// </summary>
    public class PigeonVisual : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform wingLeft;
        [SerializeField] private Transform wingRight;
        [SerializeField] private float followSharpness = 12f;
        [SerializeField] private float flapSpeed = 26f;

        private Vector3 _target;
        private bool _flying;
        private float _time;
        private Vector3 _headRest;

        private void Awake()
        {
            _target = transform.position;
            if (head != null) _headRest = head.localPosition;
        }

        public void SetTarget(Vector3 position) => _target = position;

        public void Snap(Vector3 position)
        {
            _target = position;
            transform.position = position;
        }

        public void SetFlying(bool flying)
        {
            if (flying != _flying)
            {
                Maoshi.Audio.AudioManager.PlayAt("flap", transform.position, 0.9f, 0.1f, 35f);
                if (!flying && Random.value < 0.5f) Maoshi.Audio.AudioManager.PlayAt("coo", transform.position, 0.6f, 0.1f, 25f);
            }
            _flying = flying;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;

            Vector3 previous = transform.position;
            transform.position = Vector3.Lerp(previous, _target, 1f - Mathf.Exp(-followSharpness * dt));

            Vector3 velocity = transform.position - previous;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.000004f)
            {
                Quaternion look = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-8f * dt));
            }

            float wing;
            if (_flying)
            {
                wing = Mathf.Sin(_time * flapSpeed) * 60f;
                if (head != null) head.localPosition = _headRest;
            }
            else
            {
                wing = 0f;
                // Клюёт: голова резко вниз-вперёд каждые ~0.9 с.
                float peck = Mathf.Max(0f, Mathf.Sin(_time * 7f)) * (Mathf.Sin(_time * 1.3f) > 0.2f ? 1f : 0f);
                if (head != null) head.localPosition = _headRest + new Vector3(0f, -0.08f, 0.06f) * peck;
            }

            if (wingLeft != null) wingLeft.localRotation = Quaternion.Euler(0f, 0f, wing);
            if (wingRight != null) wingRight.localRotation = Quaternion.Euler(0f, 0f, -wing);
        }
    }
}
