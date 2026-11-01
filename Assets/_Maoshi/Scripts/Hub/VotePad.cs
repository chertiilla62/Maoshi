using UnityEngine;

namespace Maoshi.Hub
{
    /// <summary>
    /// Круг голосования во дворе: номер, название мини-игры, число голосов, свой цвет.
    /// Во время выбора победителя круги гаснут, подсвеченный «барабаном» вспыхивает, победитель пульсирует.
    /// Чистый визуал — голоса считает HubVoting на сервере по позициям котов.
    /// </summary>
    public class VotePad : MonoBehaviour
    {
        public enum State { Idle, Lit, Dim, Winner }

        [SerializeField] private float radius = 1.5f;
        [SerializeField] private Renderer disc;
        [SerializeField] private TextMesh label;
        [SerializeField] private Color idleColor = new(0.95f, 0.85f, 0.45f);
        [SerializeField] private Color winnerColor = new(0.45f, 0.95f, 0.5f);

        [Header("Афиша за кругом")]
        [SerializeField] private Renderer posterFrame;
        [SerializeField] private Renderer posterHeader;
        [SerializeField] private TextMesh posterHead;
        [SerializeField] private TextMesh posterTitle;
        [SerializeField] private TextMesh posterTag;
        [SerializeField] private TextMesh posterVotes;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _block;
        private Color _color;
        private State _state;
        private float _stateTime;
        private Vector3 _discScale;
        private bool _hasScale;

        public float Radius => radius;

        /// <summary>Стоит ли точка на круге (по горизонтали, высота не важна в пределах 2 м).</summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - transform.position;
            if (offset.y < -0.5f || offset.y > 2f) return false;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>Старый вызов: без номера и цвета.</summary>
        public void Show(string title, int votes, bool isWinner) =>
            Show(0, title, votes, isWinner ? winnerColor : idleColor, isWinner ? State.Winner : State.Idle);

        /// <summary>Показать вариант: номер, название, голоса, цвет и состояние подсветки.</summary>
        public void Show(int number, string title, int votes, Color color, State state)
        {
            gameObject.SetActive(true);
            if (!_hasScale && disc != null)
            {
                _discScale = disc.transform.localScale;
                _hasScale = true;
            }

            if (label != null)
            {
                string head = number > 0 ? $"{number}. {title}" : title;
                string count = votes == 0 ? "нет голосов" : votes == 1 ? "1 голос" : votes < 5 ? $"{votes} голоса" : $"{votes} голосов";
                label.text = $"{head}\n{count}";
                label.color = state == State.Dim ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            }

            if (state != _state) _stateTime = Time.time;
            _state = state;
            _color = color;
            Apply();
        }

        /// <summary>Афиша: номер и жанр в шапке, название, о чём игра, кто голосует (строка с цветами игроков).</summary>
        public void ShowPoster(int number, string genre, Color genreColor, string title, string tagline, string voters, Color padColor)
        {
            bool dim = _state == State.Dim;
            float a = dim ? 0.35f : 1f;
            Color ink = new(0.13f, 0.09f, 0.16f, a);
            if (posterHead != null) { posterHead.text = $"{number}  ·  {genre.ToUpperInvariant()}"; posterHead.color = new Color(1f, 1f, 1f, a); }
            if (posterTitle != null) { posterTitle.text = title; posterTitle.color = ink; }
            if (posterTag != null) { posterTag.text = tagline; posterTag.color = new Color(0.27f, 0.2f, 0.27f, a); }
            if (posterVotes != null) { posterVotes.text = voters; posterVotes.color = ink; }
            Tint(posterHeader, dim ? Color.Lerp(genreColor, Color.gray, 0.6f) : genreColor, 0f);
            Tint(posterFrame, dim ? Color.Lerp(padColor, Color.gray, 0.6f) : padColor, _state == State.Winner ? 0.6f : 0f);
        }

        private void Tint(Renderer target, Color color, float glow)
        {
            if (target == null) return;
            _block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _block.SetColor(EmissionId, color * glow);
            target.SetPropertyBlock(_block);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            if (_state == State.Winner || _state == State.Lit) Apply();
        }

        private void Apply()
        {
            if (disc == null) return;
            Color c = _color;
            float scale = 1f;
            float glow = 0f;
            switch (_state)
            {
                case State.Dim:
                    c = Color.Lerp(_color, new Color(0.2f, 0.2f, 0.22f), 0.65f);
                    break;
                case State.Lit:
                    float flash = Mathf.Clamp01(1f - (Time.time - _stateTime) / 0.25f);
                    c = Color.Lerp(_color, Color.white, 0.35f + 0.4f * flash);
                    scale = 1.06f + 0.08f * flash;
                    glow = 0.6f;
                    break;
                case State.Winner:
                    float pulse = 0.5f + 0.5f * Mathf.Sin((Time.time - _stateTime) * 9f);
                    c = Color.Lerp(_color, Color.white, 0.2f + 0.3f * pulse);
                    scale = 1.1f + 0.06f * pulse;
                    glow = 0.5f + 0.6f * pulse;
                    break;
            }

            if (_hasScale) disc.transform.localScale = new Vector3(_discScale.x * scale, _discScale.y, _discScale.z * scale);
            _block ??= new MaterialPropertyBlock();
            disc.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            _block.SetColor(EmissionId, c * glow);
            disc.SetPropertyBlock(_block);
        }

        private void LateUpdate()
        {
            // Надпись всегда повёрнута к камере.
            var main = Camera.main;
            if (label != null && main != null && label.GetComponent<FaceCamera>() == null) label.transform.rotation = main.transform.rotation;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
