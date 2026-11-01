using UnityEngine;

namespace Maoshi.MiniGames.FlyHunt
{
    /// <summary>
    /// Как муха выглядит у каждого игрока: плавно догоняет серверную позицию, смотрит по ходу полёта,
    /// машет крыльями и чуть подрагивает. Сама муха больше обычной — чтобы её было видно через весь двор.
    /// </summary>
    public class FlyVisual : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform wingLeft;
        [SerializeField] private Transform wingRight;
        [SerializeField] private float followSharpness = 14f;
        [SerializeField] private float flapSpeed = 45f;
        [SerializeField] private float flapAngle = 55f;
        [Tooltip("Тень под мухой — помогает понять, на какой она высоте.")]
        [SerializeField] private Transform shadow;

        private Vector3 _target;
        private Renderer[] _renderers;
        private float _flap;

        private void Awake()
        {
            _target = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void SetTarget(Vector3 position) => _target = position;

        public void Snap(Vector3 position)
        {
            _target = position;
            transform.position = position;
        }

        public void SetVisible(bool visible)
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in _renderers) renderer.enabled = visible;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 previous = transform.position;
            transform.position = Vector3.Lerp(previous, _target, 1f - Mathf.Exp(-followSharpness * dt));

            // Смотрим по ходу полёта (только по горизонтали).
            Vector3 velocity = transform.position - previous;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.000001f)
            {
                Quaternion look = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-10f * dt));
            }

            // Крылья.
            _flap += dt * flapSpeed;
            float angle = Mathf.Sin(_flap) * flapAngle;
            if (wingLeft != null) wingLeft.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (wingRight != null) wingRight.localRotation = Quaternion.Euler(0f, 0f, -angle);

            // Подрагивание тела.
            if (body != null) body.localPosition = new Vector3(0f, Mathf.Sin(_flap * 0.21f) * 0.03f, 0f);

            // Тень всегда на земле под мухой.
            if (shadow != null)
            {
                Vector3 ground = transform.position;
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                    ground = hit.point + Vector3.up * 0.02f;
                else
                    ground.y = 0.02f;
                shadow.SetPositionAndRotation(ground, Quaternion.identity);
            }
        }
    }
}
