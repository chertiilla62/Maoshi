using System.Collections.Generic;
using Maoshi.Network;
using Maoshi.Session;
using UnityEngine;

namespace Maoshi.Hub
{
    /// <summary>
    /// Флажок зоны во дворе: красится в цвет владельца, серый — если зона свободна.
    /// Зоны, которых нет в этой партии, прячут флаг (табличка остаётся). Флаг зоны, за которую
    /// сейчас идёт аукцион, подпрыгивает и машет сильнее. Табличка: название, хозяин и цена перекупа.
    /// Ещё служит точкой перекупа: подошёл к флажку чужой зоны — можно её перекупить (SessionManager).
    /// </summary>
    public class ZoneMarker : MonoBehaviour
    {
        private static readonly List<ZoneMarker> All = new();

        public int ZoneId => zoneId;

        public static ZoneMarker Find(int id)
        {
            foreach (var marker in All) if (marker != null && marker.zoneId == id) return marker;
            return null;
        }

        /// <summary>Ближайший флажок зоны, которая есть в партии, в пределах радиуса по горизонтали.</summary>
        public static ZoneMarker Nearest(Vector3 position, float radius)
        {
            ZoneMarker best = null;
            float bestDistance = radius;
            foreach (var marker in All)
            {
                if (marker == null || !marker._inPlay) continue;
                float d = marker.HorizontalDistance(position);
                if (d <= bestDistance) { bestDistance = d; best = marker; }
            }
            return best;
        }

        /// <summary>Расстояние по горизонтали (флажки бывают на крышах и балконах — высота не важна).</summary>
        public float HorizontalDistance(Vector3 position)
        {
            Vector3 d = position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);
        [SerializeField] private int zoneId;
        [SerializeField] private Transform flag;
        [SerializeField] private Renderer cloth;
        [SerializeField] private TextMesh label;
        [Tooltip("Пунктирный круг зоны на земле/крыше — красится в цвет хозяина.")]
        [SerializeField] private Renderer[] ring;
        [Tooltip("Столб света над флажком — виден через весь двор.")]
        [SerializeField] private Renderer beam;

        private static readonly Color FreeColor = new(0.85f, 0.83f, 0.78f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private float _nextRefresh;
        private bool _inPlay = true;
        private bool _auction;
        private Color _color = FreeColor;
        private Vector3 _flagRest;
        private float _phase;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (flag != null) _flagRest = flag.localPosition;
            _phase = zoneId * 1.7f;
            if (label != null) label.text = YardZones.NameOf(zoneId);
            Apply();
        }

        private void Update()
        {
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + 0.3f;
                Refresh();
            }

            // Столб света зоны на аукционе пульсирует.
            if (_auction && beam != null) ApplyBeam(0.6f + 0.8f * Mathf.Abs(Mathf.Sin(Time.time * 3f)));

            if (flag == null || !_inPlay) return;
            float wave = Mathf.Sin(Time.time * (_auction ? 9f : 3f) + _phase) * (_auction ? 18f : 7f);
            flag.localRotation = Quaternion.Euler(0f, wave, 0f);
            flag.localPosition = _flagRest + (_auction ? Vector3.up * (Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.3f) : Vector3.zero);
        }

        private void Refresh()
        {
            var session = SessionManager.Instance;
            bool inPlay = true;
            Color color = FreeColor;
            bool auction = false;
            string text = YardZones.NameOf(zoneId);

            if (session != null && session.Zones != null && session.Zones.Count > 0)
            {
                inPlay = false;
                foreach (var zone in session.Zones)
                {
                    if (zone.ZoneId != zoneId) continue;
                    inPlay = true;
                    if (zone.HasOwner)
                    {
                        var owner = NetworkPlayer.FindByClientId(zone.Owner);
                        if (owner != null) color = owner.Color;
                        text += $"\n<color=#{ColorUtility.ToHtmlStringRGB(color)}>●</color> {(owner != null ? owner.DisplayName : "?")}  ·  перекуп {session.BuyoutPrice(zone)} рыб";
                    }
                    else text += "\nсвободна";
                }

                auction = session.AuctionZone.Value == zoneId;
            }

            if (label != null && label.text != text)
            {
                label.richText = true;
                label.text = text;
            }
            if (inPlay == _inPlay && color == _color && auction == _auction) return;
            _inPlay = inPlay;
            _color = color;
            _auction = auction;
            Apply();
        }

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Apply()
        {
            // Зоны, которых нет в этой партии, прячем целиком — и табличку, и столб (их число зависит от игроков).
            if (flag != null) flag.gameObject.SetActive(_inPlay);
            if (label != null) label.gameObject.SetActive(_inPlay);
            foreach (Transform child in transform)
                if (child.name == "Pole" || child.name == "PoleTop") child.gameObject.SetActive(_inPlay);
            if (ring != null)
                foreach (var r in ring)
                    if (r != null) r.gameObject.SetActive(_inPlay);
            if (beam != null) beam.gameObject.SetActive(_inPlay);
            if (cloth != null) Tint(cloth, _color, 0f);
            bool free = _color == FreeColor;
            if (ring != null)
                foreach (var r in ring) Tint(r, _color, free ? 0.1f : 0.6f);
            ApplyBeam(1f);
        }

        private void ApplyBeam(float pulse)
        {
            if (beam == null) return;
            bool free = _color == FreeColor;
            Color c = _color;
            c.a = (free ? 0.12f : 0.35f) * pulse;
            beam.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            beam.SetPropertyBlock(_block);
        }

        private void Tint(Renderer r, Color color, float glow)
        {
            if (r == null) return;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _block.SetColor(EmissionId, color * glow);
            r.SetPropertyBlock(_block);
        }
    }
}
