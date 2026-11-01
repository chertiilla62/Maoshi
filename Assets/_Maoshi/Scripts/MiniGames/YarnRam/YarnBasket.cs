using UnityEngine;

namespace Maoshi.MiniGames.YarnRam
{
    /// <summary>
    /// Корзинка игрока на краю крыши. Плетёный бортик красится в цвет хозяина, над корзинкой — имя и счёт.
    /// Клубок, вкатившийся в корзинку, приносит очки её хозяину (проверяет сервер в YarnRamMode).
    /// Бортик без коллайдера — клубок закатывается свободно, а кот может встать в чужую корзинку и загородить её.
    /// </summary>
    public class YarnBasket : MonoBehaviour
    {
        [SerializeField] private float radius = 1.05f;
        [SerializeField] private Renderer[] tinted;
        [SerializeField] private TextMesh label;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _block;
        private float _flashUntil;
        private Color _color = Color.white;

        /// <summary>Центр клубка внутри корзинки (по горизонтали) и не высоко над крышей.</summary>
        public bool Contains(Vector3 point, float maxHeight)
        {
            Vector3 d = point - transform.position;
            if (d.y > maxHeight || d.y < -1f) return false;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        public void Show(bool visible) => gameObject.SetActive(visible);

        public void SetOwner(Color color, string text)
        {
            _color = color;
            if (label != null && label.text != text) label.text = text;
            Apply();
        }

        /// <summary>Вспышка, когда в корзинку закатился клубок.</summary>
        public void Flash() => _flashUntil = Time.time + 0.6f;

        private void Update()
        {
            if (Time.time < _flashUntil + 0.1f) Apply();
        }

        private void Apply()
        {
            if (tinted == null) return;
            _block ??= new MaterialPropertyBlock();
            float flash = Mathf.Clamp01((_flashUntil - Time.time) / 0.6f);
            Color c = Color.Lerp(_color, Color.white, flash * 0.6f);
            foreach (var r in tinted)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, c);
                _block.SetColor(ColorId, c);
                _block.SetColor(EmissionId, c * (0.25f + flash * 1.5f));
                r.SetPropertyBlock(_block);
            }
        }
    }
}
