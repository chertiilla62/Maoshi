using UnityEngine;

namespace Maoshi.MiniGames.MouseHoles
{
    /// <summary>
    /// Норка с мышью: чистый визуал. Мышь выглядывает из норки, когда сервер её «выпустил».
    /// Какая норка активна, решает MouseHolesMode.
    /// </summary>
    public class MouseHole : MonoBehaviour
    {
        [SerializeField] private Transform mouse;
        [SerializeField] private Renderer[] mouseRenderers;
        [SerializeField] private Color normalColor = new(0.6f, 0.6f, 0.62f);
        [SerializeField] private Color goldenColor = new(1f, 0.8f, 0.15f);
        [SerializeField] private float hiddenY = -0.35f;
        [SerializeField] private float shownY = 0.05f;
        [SerializeField] private float popSpeed = 10f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private bool _visible;

        public void SetMouse(bool visible, bool golden)
        {
            if (visible && !_visible) Maoshi.Audio.AudioManager.PlayAt("squeak", transform.position, golden ? 1f : 0.7f, 0.15f, 25f);
            _visible = visible;
            if (!visible) return;

            _block ??= new MaterialPropertyBlock();
            Color color = golden ? goldenColor : normalColor;
            foreach (var mouseRenderer in mouseRenderers)
            {
                if (mouseRenderer == null) continue;
                mouseRenderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                mouseRenderer.SetPropertyBlock(_block);
            }
        }

        private void Update()
        {
            if (mouse == null) return;

            // Мышь плавно выглядывает и прячется.
            Vector3 local = mouse.localPosition;
            float target = _visible ? shownY : hiddenY;
            local.y = Mathf.MoveTowards(local.y, target, popSpeed * Time.deltaTime);
            mouse.localPosition = local;

            // Видна, только когда хоть немного над землёй.
            bool show = local.y > hiddenY + 0.01f;
            if (mouse.gameObject.activeSelf != show) mouse.gameObject.SetActive(show);

            // Смешно вертит головой, пока на виду.
            if (_visible) mouse.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 6f) * 35f, 0f);
        }
    }
}
