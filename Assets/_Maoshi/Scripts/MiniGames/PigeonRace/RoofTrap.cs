using Maoshi.Audio;
using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.PigeonRace
{
    /// <summary>
    /// Ловушка на крышах «Гонки за голубем». Движется по расписанию от сетевого времени — у всех игроков одинаково,
    /// поэтому по сети её не синхронизируем. Удар проверяет у себя владелец кота (как он видит ловушку, так и попадает)
    /// и отправляет его через ReceiveHitRpc — анимацию удара видят все.
    ///
    /// Виды:
    /// - Swing — бельё на верёвке качается поперёк крыши (pivot вокруг своей оси Z);
    /// - Spin — вертушка-флюгер крутит планку над крышей (pivot вокруг Y), перепрыгивай;
    /// - Slide — тележка ездит туда-сюда (pivot вдоль своей оси X);
    /// - Sink — доска трясётся и проваливается, потом возвращается;
    /// - Steam — вентиляция раз в несколько секунд выбрасывает пар и подкидывает кота;
    /// - Drop — с балкона сверху падает цветочный горшок: сначала растёт тень-метка, потом удар и оглушение.
    /// Зона удара — единичный куб в координатах hitVolume (то есть сам объект-коробка) плюс запас.
    /// </summary>
    public class RoofTrap : MonoBehaviour
    {
        public enum Kind { Swing, Spin, Slide, Sink, Steam, Drop }

        [SerializeField] private Kind kind;
        [Tooltip("Что двигается (для Sink — сама доска с кинематическим Rigidbody).")]
        [SerializeField] private Transform pivot;
        [Tooltip("Коробка, касание которой — удар. Пусто — ловушка не бьёт (Sink).")]
        [SerializeField] private Transform hitVolume;
        [SerializeField] private float period = 3f;
        [Tooltip("Сдвиг по фазе, доля периода 0..1.")]
        [SerializeField] private float phase;
        [Tooltip("Swing — размах в градусах; Slide — ход в метрах в каждую сторону; Sink — глубина; Spin — градусов в секунду.")]
        [SerializeField] private float amount = 50f;
        [SerializeField] private float hitForce = 6f;
        [SerializeField] private float hitLift = 3f;
        [SerializeField] private bool knockDown;
        [SerializeField] private float margin = 0.22f;

        [Header("Пар / провал")]
        [SerializeField] private ParticleSystem steam;
        [Tooltip("Steam: доля периода с выбросом; Sink: доля периода под водой... то есть внизу.")]
        [SerializeField] private float activeFraction = 0.18f;
        [Tooltip("Steam/Sink: сколько секунд до срабатывания — предупреждение (шипение/тряска).")]
        [SerializeField] private float warnSeconds = 0.8f;
        [Tooltip("Drop: тень на крыше, куда упадёт горшок.")]
        [SerializeField] private Transform marker;

        private Quaternion _restRotation;
        private Vector3 _restPosition;
        private Rigidbody _body;
        private Vector3 _lastHitPos;
        private Vector3 _hitVelocity;
        private float _cooldownUntil;
        private int _lastCycle = -1;
        private bool _wasActive;
        private bool _warned;
        private bool _dropping;
        private Vector3 _markerScale;

        private static double Clock =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        private void Awake()
        {
            if (pivot == null) pivot = transform;
            _restRotation = pivot.localRotation;
            _restPosition = pivot.localPosition;
            _body = pivot.GetComponent<Rigidbody>();
            if (_body != null)
            {
                _body.isKinematic = true;
                _body.interpolation = RigidbodyInterpolation.Interpolate;
            }
            if (hitVolume != null) _lastHitPos = hitVolume.position;
            if (marker != null) _markerScale = marker.localScale;
        }

        private void FixedUpdate()
        {
            // Доска и тележка с коллайдером двигаются физикой: кот на доске падает, тележка толкает.
            if (_body != null) Animate(Time.fixedDeltaTime);
        }

        private void Update()
        {
            if (_body == null) Animate(Time.deltaTime);
            CheckLocalCat();
        }

        private void Animate(float dt)
        {
            double t = Clock;
            float cyc = (float)((t / period + phase) % 1.0);
            if (cyc < 0f) cyc += 1f;

            switch (kind)
            {
                case Kind.Swing:
                {
                    float angle = Mathf.Sin(cyc * Mathf.PI * 2f) * amount;
                    Set(_restPosition, _restRotation * Quaternion.Euler(0f, 0f, angle));
                    break;
                }
                case Kind.Spin:
                {
                    float angle = (float)((t * amount + phase * 360.0) % 360.0);
                    Set(_restPosition, _restRotation * Quaternion.Euler(0f, angle, 0f));
                    break;
                }
                case Kind.Slide:
                {
                    // Плавно доезжает до края, стоит чуть-чуть и едет обратно.
                    float s = Mathf.Sin(cyc * Mathf.PI * 2f);
                    s = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.6f);
                    Set(_restPosition + _restRotation * Vector3.right * (s * amount), _restRotation);
                    break;
                }
                case Kind.Sink:
                {
                    float down = 0f, shake = 0f;
                    float sinkAt = 1f - activeFraction;
                    float warnAt = sinkAt - warnSeconds / period;
                    if (cyc >= warnAt && cyc < sinkAt) shake = (cyc - warnAt) / (sinkAt - warnAt);
                    else if (cyc >= sinkAt)
                    {
                        float local = (cyc - sinkAt) / activeFraction;
                        down = Mathf.Clamp01(local / 0.15f) * Mathf.Clamp01((1f - local) / 0.25f);
                    }
                    Quaternion wobble = Quaternion.Euler(Mathf.Sin((float)t * 41f) * 4f * shake, 0f, Mathf.Cos((float)t * 37f) * 4f * shake);
                    Set(_restPosition + Vector3.down * (amount * down) + Vector3.down * (0.03f * shake), _restRotation * wobble);

                    bool active = down > 0.3f;
                    if (shake > 0f && !_warned) { _warned = true; AudioManager.PlayAt("squeak", pivot.position, 0.5f, 0.1f, 20f); }
                    if (active && !_wasActive) AudioManager.PlayAt("crash", pivot.position, 0.35f, 0.1f, 20f);
                    if (!active && shake == 0f) _warned = false;
                    _wasActive = active;
                    break;
                }
                case Kind.Drop:
                {
                    float tc = cyc * period;
                    float warnStart = period - warnSeconds - 0.9f;
                    float land = warnStart + warnSeconds;
                    float fallStart = land - 0.42f;
                    Vector3 top = _restPosition + Vector3.up * amount;
                    bool visible = tc >= fallStart;
                    _dropping = tc >= fallStart && tc < land;
                    if (tc < fallStart) pivot.localPosition = top;
                    else if (tc < land)
                    {
                        float u = (tc - fallStart) / (land - fallStart);
                        pivot.localPosition = Vector3.Lerp(top, _restPosition, u * u);
                    }
                    else pivot.localPosition = _restPosition;
                    // После падения горшок «разбит»: сплющивается и исчезает.
                    float broken = tc >= land ? Mathf.Clamp01((tc - land) / 0.5f) : 0f;
                    pivot.localScale = visible ? new Vector3(1f + broken * 0.5f, Mathf.Max(0.05f, 1f - broken), 1f + broken * 0.5f) : Vector3.zero;
                    if (marker != null)
                    {
                        bool warn = tc >= warnStart && tc < land;
                        float k = warn ? Mathf.Clamp01((tc - warnStart) / warnSeconds) : 0f;
                        marker.localScale = warn ? _markerScale * (0.35f + 0.65f * k) : Vector3.zero;
                    }
                    bool landed = tc >= land;
                    if (landed && !_wasActive) AudioManager.PlayAt("vase_break", pivot.position, 0.7f, 0.1f, 25f);
                    _wasActive = landed;
                    break;
                }
                case Kind.Steam:
                {
                    float burstAt = 1f - activeFraction;
                    float warnAt = burstAt - warnSeconds / period;
                    bool warn = cyc >= warnAt && cyc < burstAt;
                    bool active = cyc >= burstAt;
                    int cycle = (int)(t / period + phase);
                    if (steam != null)
                    {
                        if (warn && Random.value < dt * 12f) steam.Emit(1);
                        if (active) steam.Emit(Mathf.Max(1, Mathf.RoundToInt(dt * 90f)));
                    }
                    if (active && !_wasActive && cycle != _lastCycle)
                    {
                        _lastCycle = cycle;
                        AudioManager.PlayAt("hiss", transform.position, 0.6f, 0.15f, 22f);
                    }
                    _wasActive = active;
                    break;
                }
            }

            if (hitVolume != null)
            {
                Vector3 p = hitVolume.position;
                if (dt > 0f) _hitVelocity = Vector3.Lerp(_hitVelocity, (p - _lastHitPos) / dt, 0.5f);
                _lastHitPos = p;
            }
        }

        private void Set(Vector3 localPosition, Quaternion localRotation)
        {
            if (_body != null && pivot.parent != null)
            {
                _body.MovePosition(pivot.parent.TransformPoint(localPosition));
                _body.MoveRotation(pivot.parent.rotation * localRotation);
            }
            else if (_body != null)
            {
                _body.MovePosition(localPosition);
                _body.MoveRotation(localRotation);
            }
            else
            {
                pivot.localPosition = localPosition;
                pivot.localRotation = localRotation;
            }
        }

        /// <summary>Проверка только своего кота: попал в зону удара — отлетаешь.</summary>
        private void CheckLocalCat()
        {
            if (hitVolume == null || Time.time < _cooldownUntil) return;
            if (kind == Kind.Steam && !_wasActive) return;
            if (kind == Kind.Drop && !_dropping) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            var cat = CatController.FindByOwner(nm.LocalClientId);
            if (cat == null || !cat.IsOwner) return;

            Vector3 center = cat.transform.position + Vector3.up * 0.3f;
            Vector3 local = hitVolume.InverseTransformPoint(center);
            Vector3 scale = hitVolume.lossyScale;
            if (Mathf.Abs(local.x) > 0.5f + margin / Mathf.Max(0.01f, Mathf.Abs(scale.x))) return;
            if (Mathf.Abs(local.y) > 0.5f + margin / Mathf.Max(0.01f, Mathf.Abs(scale.y))) return;
            if (Mathf.Abs(local.z) > 0.5f + margin / Mathf.Max(0.01f, Mathf.Abs(scale.z))) return;

            Vector3 push;
            if (kind == Kind.Steam || kind == Kind.Drop)
            {
                Vector3 away = cat.transform.position - hitVolume.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0f;
                push = away.normalized * hitForce + Vector3.up * hitLift;
            }
            else
            {
                Vector3 v = _hitVelocity;
                v.y = 0f;
                if (v.sqrMagnitude < 0.04f)
                {
                    v = cat.transform.position - hitVolume.position;
                    v.y = 0f;
                }
                push = v.normalized * hitForce + Vector3.up * hitLift;
            }

            _cooldownUntil = Time.time + 1.0f;
            cat.ReceiveHitRpc(push, knockDown);
        }
    }
}
