using UnityEngine;

namespace Maoshi.MiniGames.LastInBox
{
    /// <summary>
    /// Картонная коробка из четырёх стенок и дна. Место, размер и видимость задаёт LastInBoxMode.
    /// Коробка не ездит: она исчезает и «выпрыгивает» в новом месте (короткая анимация масштаба).
    /// Кинематический Rigidbody — чтобы стенки честно толкали котов. Через стенку можно перепрыгнуть.
    /// </summary>
    public class BoxVisual : MonoBehaviour
    {
        [SerializeField] private Transform wallNorth;
        [SerializeField] private Transform wallSouth;
        [SerializeField] private Transform wallEast;
        [SerializeField] private Transform wallWest;
        [SerializeField] private Transform floor;
        [SerializeField] private float wallHeight = 0.6f;
        [SerializeField] private float wallThickness = 0.12f;
        [Tooltip("Длительность «выпрыгивания» при появлении, с.")]
        [SerializeField] private float popDuration = 0.3f;

        private Rigidbody _body;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private bool _visible = true;
        private float _popTimer = -1f;

        public bool IsVisible => _visible;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.None;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>Внутренний размер коробки (сторона квадрата, м).</summary>
        public void SetSize(float size)
        {
            float half = size * 0.5f + wallThickness * 0.5f;
            float length = size + wallThickness * 2f;

            Place(wallNorth, new Vector3(0f, wallHeight * 0.5f, half), new Vector3(length, wallHeight, wallThickness));
            Place(wallSouth, new Vector3(0f, wallHeight * 0.5f, -half), new Vector3(length, wallHeight, wallThickness));
            Place(wallEast, new Vector3(half, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, size));
            Place(wallWest, new Vector3(-half, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, size));
            Place(floor, new Vector3(0f, 0.01f, 0f), new Vector3(size, 0.02f, size));
        }

        /// <summary>Поставить коробку в точку (высота сохраняется).</summary>
        public void SetPosition(Vector3 position)
        {
            var target = new Vector3(position.x, transform.position.y, position.z);
            transform.position = target;
            if (_body != null) _body.position = target;
        }

        /// <summary>Показать (с «выпрыгиванием») или спрятать коробку.</summary>
        public void SetVisible(bool visible, bool animate = true)
        {
            if (_visible == visible) return;
            _visible = visible;

            if (_renderers != null)
                foreach (var renderer in _renderers) renderer.enabled = visible;
            if (_colliders != null)
                foreach (var collider in _colliders) collider.enabled = visible;

            if (visible && animate)
            {
                _popTimer = 0f;
                transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            }
            else
            {
                _popTimer = -1f;
                transform.localScale = Vector3.one;
            }
        }

        private void Update()
        {
            if (_popTimer < 0f) return;

            _popTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_popTimer / popDuration);
            // Лёгкий «перелёт» за 1 и обратно — коробка выпрыгивает.
            float scale = t < 1f ? Mathf.LerpUnclamped(0.2f, 1f, BackOut(t)) : 1f;
            transform.localScale = new Vector3(scale, Mathf.Min(scale, 1.15f), scale);
            if (t >= 1f) _popTimer = -1f;
        }

        private static float BackOut(float t)
        {
            const float c = 1.70158f;
            float x = t - 1f;
            return 1f + (c + 1f) * x * x * x + c * x * x;
        }

        private static void Place(Transform part, Vector3 localPosition, Vector3 scale)
        {
            if (part == null) return;
            part.localPosition = localPosition;
            part.localScale = scale;
        }
    }
}
