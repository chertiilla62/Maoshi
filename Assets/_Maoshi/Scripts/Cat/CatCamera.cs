using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.Cat
{
    /// <summary>
    /// Камера от третьего лица за своим котом (в духе Stray).
    /// - Мышь крутит камеру всегда, курсор спрятан. Курсор появляется в меню, в окнах с кнопками и пока зажат Alt.
    ///   Если курсор свободен — крутить можно зажатой правой кнопкой. Геймпад — правый стик.
    /// - Колёсико — ближе/дальше. Вблизи камера опускается к коту, вдали — поднимается.
    /// - Камера не проходит сквозь стены: отодвигается к коту, если за спиной препятствие.
    /// </summary>
    public class CatCamera : MonoBehaviour
    {
        [Header("Расстояние")]
        [SerializeField] private float startDistance = 6f;
        [SerializeField] private float minDistance = 1.6f;
        [SerializeField] private float maxDistance = 12f;
        [SerializeField] private float zoomStep = 0.9f;
        [SerializeField] private float zoomSharpness = 10f;

        [Header("Высота взгляда")]
        [Tooltip("Точка, на которую смотрит камера, над котом: вблизи / вдали.")]
        [SerializeField] private float focusHeightNear = 0.5f;
        [SerializeField] private float focusHeightFar = 1.2f;

        [Header("Повороты")]
        [SerializeField] private float startPitch = 18f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 75f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float stickSensitivity = 160f;
        [SerializeField] private float followSharpness = 14f;

        [Header("Стены")]
        [SerializeField] private float collisionRadius = 0.22f;

        private Transform _target;
        private float _yaw;
        private float _pitch;
        private float _targetDistance;
        private float _distance;
        private float _collisionDistance;
        private Vector3 _focus;
        private bool _cursorLocked;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // Тряска (удар, падение) и толчок точки взгляда (приземление): чувствуется вес.
        private float _trauma;
        private Vector3 _kick;
        private Vector3 _kickVelocity;

        /// <summary>Тряхнуть камеру: 0.2 — лёгкий толчок, 0.6 — сильный удар.</summary>
        public void Shake(float amount)
        {
            if (Maoshi.Settings.GameSettings.ReduceMotion) amount *= 0.3f;
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        /// <summary>Толкнуть точку взгляда (например, вниз при жёстком приземлении) — вернётся пружиной.</summary>
        public void Kick(Vector3 velocity) => _kickVelocity += velocity;

        /// <summary>Направление «вперёд» камеры по земле — относительно него кот бегает.</summary>
        public Vector3 PlanarForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        public Vector3 PlanarRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        /// <summary>Куда смотрит камера (для поворота головы кота).</summary>
        public Vector3 LookDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        /// <summary>Находит (или создаёт) CatCamera на главной камере сцены.</summary>
        public static CatCamera AttachToMain()
        {
            var main = Camera.main;
            if (main == null) return null;

            var catCamera = main.GetComponent<CatCamera>();
            if (catCamera == null) catCamera = main.gameObject.AddComponent<CatCamera>();

            // Мультяшная постобработка (насыщенность, свечение) работает только с включённым флажком камеры.
            var data = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null) data = main.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            return catCamera;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (target == null) return;

            // Встаём сразу за спиной кота, без плавного «подлёта» через всю карту.
            _yaw = target.eulerAngles.y;
            _pitch = startPitch;
            if (_targetDistance <= 0f) _targetDistance = startDistance;
            _distance = _targetDistance;
            _collisionDistance = _distance;
            _focus = target.position + Vector3.up * FocusHeight();
            transform.SetPositionAndRotation(_focus - Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward * _distance, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        public void ClearTarget(Transform target)
        {
            if (_target != target) return;
            _target = null;
            SetCursorLocked(false);
        }

        private void OnDisable() => SetCursorLocked(false);

        private void LateUpdate()
        {
            if (_target == null)
            {
                if (_cursorLocked) SetCursorLocked(false);
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool altHeld = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            bool wantCursor = CatInput.Blocked || CatInput.UIWantsCursor || altHeld || !Application.isFocused;
            SetCursorLocked(!wantCursor);

            Vector2 look = Vector2.zero;
            float sensitivity = Maoshi.Settings.GameSettings.MouseSensitivity;
            if (!CatInput.Blocked)
            {
                if (mouse != null && (_cursorLocked || mouse.rightButton.isPressed))
                    look += mouse.delta.ReadValue() * (mouseSensitivity * sensitivity);

                var gamepad = Gamepad.current;
                if (gamepad != null)
                    look += gamepad.rightStick.ReadValue() * (stickSensitivity * sensitivity * Time.deltaTime);

                // Колёсико: ближе / дальше (над окнами интерфейса не срабатывает).
                if (mouse != null && !CatInput.PointerOverUI)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                        _targetDistance = Mathf.Clamp(_targetDistance - Mathf.Sign(scroll) * zoomStep * (_targetDistance / 6f + 0.5f), minDistance, maxDistance);
                }
            }

            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);

            float dt = Time.deltaTime;
            _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-zoomSharpness * dt));

            // Точка взгляда догоняет кота мягко (без дрожи от физики), камера вращается вокруг неё.
            Vector3 focusTarget = _target.position + Vector3.up * FocusHeight();
            _focus = Vector3.Lerp(_focus, focusTarget, 1f - Mathf.Exp(-followSharpness * dt));

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 back = rotation * Vector3.back;

            // Стены: если между котом и камерой что-то есть — подходим ближе (сразу), отходим обратно плавно.
            float allowed = CastDistance(_focus, back, _distance);
            _collisionDistance = allowed < _collisionDistance
                ? allowed
                : Mathf.Lerp(_collisionDistance, allowed, 1f - Mathf.Exp(-5f * dt));

            // Пружина толчка и тряска.
            _kickVelocity += (-90f * _kick - 12f * _kickVelocity) * dt;
            _kick += _kickVelocity * dt;
            _trauma = Mathf.Max(0f, _trauma - dt * 2.2f);
            float shake = _trauma * _trauma;
            float tm = Time.time * 28f;
            Vector3 shakeOffset = new Vector3(Mathf.PerlinNoise(tm, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tm) - 0.5f, 0f) * (0.25f * shake);
            Quaternion shakeRotation = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(tm, 7.1f) - 0.5f) * 6f * shake);

            transform.SetPositionAndRotation(_focus + _kick + back * _collisionDistance + rotation * shakeOffset, rotation * shakeRotation);
        }

        private float FocusHeight()
        {
            float t = Mathf.InverseLerp(minDistance, maxDistance, _distance <= 0f ? startDistance : _distance);
            return Mathf.Lerp(focusHeightNear, focusHeightFar, t);
        }

        private float CastDistance(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance <= 0f) continue; // старт внутри коллайдера
                // Коты (свой и чужие) и предметы, которые толкают, камеру не отодвигают.
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;
                if (hit.collider.GetComponentInParent<CatController>() != null) continue;
                best = Mathf.Min(best, Mathf.Max(minDistance * 0.5f, hit.distance - 0.05f));
            }

            return best;
        }

        private void SetCursorLocked(bool locked)
        {
            if (_cursorLocked == locked && Cursor.lockState == (locked ? CursorLockMode.Locked : CursorLockMode.None)) return;
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
