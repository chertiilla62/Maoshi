using UnityEngine;

namespace Maoshi.MiniGames.SausageKitchen
{
    /// <summary>
    /// Хозяйка у плиты — только картинка: поворачивается по состоянию из SausageMode (сервер решает, когда),
    /// над головой знак (♪ напевает, ! смотрит), полотенце хлещет того, кого заметила.
    /// Стоит лицом к плите (+Z), смотрит в кухню (−Z).
    /// </summary>
    public class Hostess : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform towel;
        [SerializeField] private TextMesh sign;
        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private float stoveOffset = 0.9f;

        public SausageMode Mode { get; set; }

        /// <summary>Глаза — откуда сервер проверяет, виден ли кот.</summary>
        public Vector3 Eye => transform.position + Vector3.up * eyeHeight;
        /// <summary>Куда она смотрит, обернувшись.</summary>
        public Vector3 WatchForward => -transform.forward;
        public Vector3 Stove => transform.position + transform.forward * stoveOffset + Vector3.up * 1f;

        private float _yaw;
        private float _swatT = -1f;
        private Vector3 _swatTarget;
        private Quaternion _towelRest;

        private void Awake()
        {
            if (body == null) body = transform;
            if (towel != null) _towelRest = towel.localRotation;
        }

        public void Swat(Vector3 target)
        {
            _swatT = 0f;
            _swatTarget = target;
        }

        private void Update()
        {
            if (Mode == null || !Mode.IsSpawned) return;
            var state = Mode.State.Value;
            float age = Mode.StateAge;
            float turn = 0.3f;

            float target = state switch
            {
                HostessState.Warning => 22f + Mathf.Sin(age * 9f) * 4f,
                HostessState.Turning => Mathf.Lerp(22f, 180f, Mathf.SmoothStep(0f, 1f, age / turn)),
                HostessState.Watching => 180f,
                HostessState.TurningBack => Mathf.Lerp(180f, 0f, Mathf.SmoothStep(0f, 1f, age / turn)),
                _ => Mathf.Sin(Time.time * 1.7f) * 3f, // помешивает
            };
            _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-25f * Time.deltaTime));
            body.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            if (sign != null)
            {
                string text = state switch
                {
                    HostessState.Warning => "♪ ♪",
                    HostessState.Watching or HostessState.Turning => "!",
                    _ => age % 4f < 1.2f ? "♪" : "",
                };
                if (sign.text != text) sign.text = text;
                sign.color = state is HostessState.Watching or HostessState.Turning ? new Color(0.9f, 0.2f, 0.15f) : new Color(0.25f, 0.2f, 0.35f);
                sign.transform.localPosition = new Vector3(0f, 2.25f + Mathf.Sin(Time.time * 4f) * 0.04f, 0f);
                var camera = Camera.main;
                if (camera != null) sign.transform.rotation = Quaternion.LookRotation(sign.transform.position - camera.transform.position);
            }

            // Полотенце: взмах в сторону кота.
            if (towel != null)
            {
                if (_swatT >= 0f)
                {
                    _swatT += Time.deltaTime;
                    float k = Mathf.Clamp01(_swatT / 0.45f);
                    float swing = Mathf.Sin(k * Mathf.PI) * 110f;
                    towel.localRotation = _towelRest * Quaternion.Euler(-swing, 0f, swing * 0.4f);
                    if (k >= 1f) _swatT = -1f;
                }
                else towel.localRotation = Quaternion.Slerp(towel.localRotation, _towelRest, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }
    }
}
