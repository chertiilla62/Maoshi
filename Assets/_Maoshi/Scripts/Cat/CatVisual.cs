using Unity.Netcode;
using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Всё, что касается вида кота в движении:
    /// 1) (опционально) сглаживание модели чужих котов поверх сетевой интерполяции;
    /// 2) параметры аниматора: скорость, вертикальная скорость, «на земле», прыжок, нокаут —
    ///    считаются по движению кота, поэтому одинаково работают для своего и чужих котов;
    /// 3) триггеры толчка и удара из CatController (у всех игроков).
    /// «На земле» сглаживается: короткие отрывы (ступенька, кочка) не включают анимацию падения.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CatVisual : NetworkBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private Animator animator;

        [Header("Сглаживание чужих котов")]
        [Tooltip("Дополнительно сглаживать модель чужих котов. Обычно не нужно: NetworkTransform сам интерполирует каждый кадр.")]
        [SerializeField] private bool smoothRemoteModel;
        [SerializeField] private float positionSharpness = 30f;
        [SerializeField] private float rotationSharpness = 20f;
        [SerializeField] private float snapDistance = 3f;

        [Header("Анимация")]
        [SerializeField] private float groundCheckDistance = 0.3f;
        [Tooltip("Сколько секунд кот должен быть в воздухе, чтобы включилась анимация падения (без прыжка).")]
        [SerializeField] private float fallDelay = 0.18f;
        [SerializeField] private float jumpUpSpeed = 2.5f;

        [Header("Живость (процедурно поверх анимации)")]
        [Tooltip("Наклон корпуса в повороте на полной скорости, градусов.")]
        [SerializeField] private float maxLean = 13f;
        [Tooltip("Насколько повторять наклон земли (склоны, ступеньки).")]
        [SerializeField, Range(0f, 1f)] private float slopeAlign = 0.8f;
        [Tooltip("Сплющивание при приземлении и вытягивание в прыжке.")]
        [SerializeField] private float squashAmount = 0.12f;
        [SerializeField] private float headLookWeight = 0.85f;
        [SerializeField] private float tailSway = 9f;
        [Tooltip("Торможение после бега: кот откидывается назад (градусов).")]
        [SerializeField] private float brakePitch = 14f;
        [Tooltip("Занос/разворот юзом: корпус заваливается вбок (градусов).")]
        [SerializeField] private float skidRoll = 16f;
        [Header("Контакт с землёй и другими котами (этап 29)")]
        [Tooltip("Материал пыли из-под лап (полупрозрачная частица). Пусто — без пыли.")]
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Color dustColor = new(0.86f, 0.80f, 0.70f, 0.55f);
        [Tooltip("Разгон с места: кот наклоняется вперёд и вытягивается (градусов).")]
        [SerializeField] private float pushOffPitch = 6f;
        [Tooltip("Чуть крупнее голова — мультяшнее (1 — как у модели).")]
        [SerializeField] private float headScale = 1.15f;

        [Header("Безделье")]
        [Tooltip("Через сколько секунд без движения кот начинает оглядываться, зевать, тянуться.")]
        [SerializeField] private float idleActionDelay = 4f;
        [SerializeField] private float sitDelay = 15f;
        [SerializeField] private float lieDelay = 40f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int KnockedId = Animator.StringToHash("Knocked");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int PushId = Animator.StringToHash("Push");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int TurnId = Animator.StringToHash("Turn");
        private static readonly int CrouchId = Animator.StringToHash("Crouch");
        private static readonly int IdleActionId = Animator.StringToHash("IdleAction");
        private static readonly int IdleVariantId = Animator.StringToHash("IdleVariant");
        private static readonly int RestId = Animator.StringToHash("Rest");

        private CatController _controller;
        private Vector3 _modelLocalPosition;
        private Quaternion _modelLocalRotation;
        private Vector3 _smoothPosition;
        private Quaternion _smoothRotation;
        private Vector3 _lastPosition;
        private float _smoothSpeed;
        private float _smoothVerticalSpeed;
        private float _airTime;
        private bool _animGrounded = true;
        private bool _smoothing;
        private float _actionWeight;
        private const int ActionLayer = 1;
        private const int FullActionLayer = 2;
        private static readonly int PushStandId = Animator.StringToHash("PushStand");
        private static readonly int PushSideId = Animator.StringToHash("PushSide");
        private bool _hasStandPush;
        private int _pushSide;
        private float _fullActionWeight;
        /// <summary>Время с начала удара (для процедурного выпада), &lt; 0 — удара нет.</summary>
        private float _strikeT = -1f;
        private bool _strikeMoving;

        // Этап 29: шаг по скорости, разгон, пыль, стоп-кадр, толчки котов друг о друга.
        private static readonly int StrideMulId = Animator.StringToHash("StrideMul");
        private bool _hasStride;
        private float _strideMul = 1f;
        private CatRootMotionSink _sink;
        private float _pushOff;
        private ParticleSystem _dust;
        private float _dustAccumulator;
        private float _hitStopUntil;
        private float _strikeDustAt = -1f;
        private Vector3 _bump;
        private Vector3 _bumpVelocity;
        private float _lastBumpDistance = 99f;
        private float _nextBumpTime;

        // Живость.
        private Vector3 _modelScale = Vector3.one;
        private float _lastYaw;
        private float _smoothTurn;
        private float _lean;
        private float _lastForwardSpeed;
        private float _smoothForwardAccel;
        private float _brake;
        private float _drift;
        private float _turnInPlace;
        private float _stepPhase;
        private Quaternion _slope = Quaternion.identity;
        private float _squash;
        private float _squashVelocity;
        private Transform _head;
        private Transform _neck;
        private readonly System.Collections.Generic.List<Transform> _tail = new();
        private Quaternion _look = Quaternion.identity;
        private float _idleTime;
        private float _nextIdleAction;
        private int _rest;
        private bool _hasTurnParam, _hasCrouchParam, _hasIdleParams;
        private System.Random _rng;
        private CatCamera _camera;
        private Vector3 _groundNormal = Vector3.up;

        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private void Awake()
        {
            _controller = GetComponent<CatController>();
            if (model != null)
            {
                _modelLocalPosition = model.localPosition;
                _modelLocalRotation = model.localRotation;
                _modelScale = model.localScale;
                FindBones();
            }
        }

        /// <summary>Кости головы, шеи и хвоста ищем по именам (риг AnimX).</summary>
        private void FindBones()
        {
            var all = model.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                string n = t.name;
                if (n.EndsWith("_end")) continue;
                if (_head == null && n == "Head") _head = t;
                if (_neck == null && n.StartsWith("Neck")) _neck = t;
                if (n.StartsWith("Tail")) _tail.Add(t);
            }
            _tail.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            for (var p = t; p != null; p = p.parent) d++;
            return d;
        }

        public override void OnNetworkSpawn()
        {
            _smoothing = smoothRemoteModel && !IsOwner && model != null;
            SnapModel();
            _lastPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;
            _rng = new System.Random((int)NetworkObjectId * 7919 + 13);
            _nextIdleAction = idleActionDelay + (float)_rng.NextDouble() * 2f;

            if (animator != null)
            {
                foreach (var p in animator.parameters)
                {
                    if (p.nameHash == TurnId) _hasTurnParam = true;
                    if (p.nameHash == CrouchId) _hasCrouchParam = true;
                    if (p.nameHash == RestId) _hasIdleParams = true;
                    if (p.nameHash == PushStandId) _hasStandPush = true;
                    if (p.nameHash == StrideMulId) _hasStride = true;
                }
            }

            if (_controller != null)
            {
                _controller.PushPerformed += OnPush;
                _controller.HitReceived += OnHit;
                _controller.PushLanded += OnPushLanded;
            }
            if (animator != null) _sink = animator.GetComponent<CatRootMotionSink>();
            CreateDust();
        }

        public override void OnNetworkDespawn()
        {
            if (_controller != null)
            {
                _controller.PushPerformed -= OnPush;
                _controller.HitReceived -= OnHit;
                _controller.PushLanded -= OnPushLanded;
            }
        }

        private void LateUpdate()
        {
            if (model == null) return;

            if (_smoothing) SmoothModel();
            // «Стоп-кадр» на долю секунды при попадании — удар ощущается весомым.
            if (animator != null) animator.speed = Time.time < _hitStopUntil ? 0.05f : 1f;
            UpdateAnimator();
            if (!_smoothing) ApplyBodyMotion();
            ApplyLookAndTail();
        }

        // ───────────────────────── Сглаживание ─────────────────────────

        private void SnapModel()
        {
            if (model == null) return;
            _smoothPosition = transform.TransformPoint(_modelLocalPosition);
            _smoothRotation = transform.rotation * _modelLocalRotation;
            if (_smoothing) model.SetPositionAndRotation(_smoothPosition, _smoothRotation);
        }

        private void SmoothModel()
        {
            Vector3 targetPosition = transform.TransformPoint(_modelLocalPosition);
            Quaternion targetRotation = transform.rotation * _modelLocalRotation;

            if ((targetPosition - _smoothPosition).sqrMagnitude > snapDistance * snapDistance)
            {
                SnapModel();
                return;
            }

            float dt = Time.deltaTime;
            _smoothPosition = Vector3.Lerp(_smoothPosition, targetPosition, 1f - Mathf.Exp(-positionSharpness * dt));
            _smoothRotation = Quaternion.Slerp(_smoothRotation, targetRotation, 1f - Mathf.Exp(-rotationSharpness * dt));
            model.SetPositionAndRotation(_smoothPosition, _smoothRotation);
        }

        // ───────────────────────── Анимация ─────────────────────────

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 position = transform.position;
            Vector3 velocity = (position - _lastPosition) / dt;
            _lastPosition = position;

            // Сглаживаем, иначе скорость «дрожит» от кадра к кадру.
            float horizontal = new Vector2(velocity.x, velocity.z).magnitude;

            // Торможение и занос — для позы (своего и чужих котов одинаково, по движению).
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float forwardSpeed = Vector3.Dot(flatVelocity, transform.forward);
            float forwardAccel = (forwardSpeed - _lastForwardSpeed) / dt;
            _lastForwardSpeed = forwardSpeed;
            _smoothForwardAccel = Mathf.Lerp(_smoothForwardAccel, forwardAccel, 1f - Mathf.Exp(-12f * dt));
            float sideSpeed = Vector3.Dot(flatVelocity, transform.right);
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, horizontal, 1f - Mathf.Exp(-10f * dt));
            _smoothVerticalSpeed = Mathf.Lerp(_smoothVerticalSpeed, velocity.y, 1f - Mathf.Exp(-12f * dt));

            bool touching = CheckGrounded();
            _airTime = touching ? 0f : _airTime + dt;

            bool wasAirborne = !_animGrounded;

            // Прыжок: только что стояли и резко пошли вверх.
            if (_animGrounded && !touching && _smoothVerticalSpeed > jumpUpSpeed)
            {
                _squashVelocity += 6f; // вытянулся в прыжке
                animator.SetTrigger(JumpId);
                _animGrounded = false;
            }
            else if (touching)
            {
                _animGrounded = true;
            }
            else if (_airTime > fallDelay)
            {
                // Сошли с края или подбросило — включаем падение только после короткой задержки.
                _animGrounded = false;
            }

            bool knocked = _controller != null && _controller.State.Value == CatState.Knocked;
            bool crouch = _controller != null && _controller.Crouching.Value;

            // Скорость поворота: + вправо, − влево (для клипов бега с поворотом и наклона корпуса).
            float yaw = transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            _lastYaw = yaw;
            // На месте клип поворота включается уже от умеренной скорости поворота; на бегу — как раньше.
            float turnScale = _smoothSpeed < 1.5f ? 150f : 220f;
            _smoothTurn = Mathf.Lerp(_smoothTurn, Mathf.Clamp(turnRate / turnScale, -1f, 1f), 1f - Mathf.Exp(-18f * dt));
            _turnInPlace = _smoothSpeed < 1.2f ? Mathf.Clamp01(Mathf.Abs(turnRate) / 120f) : 0f;

            // Тормозит лапами (после бега или в развороте) и занос боком.
            bool groundedNow = touching || _animGrounded;
            float brakeTarget = groundedNow && horizontal > 1f ? Mathf.Clamp01((-_smoothForwardAccel - 4f) / 14f) : 0f;
            _brake = Mathf.Lerp(_brake, brakeTarget, 1f - Mathf.Exp(-(brakeTarget > _brake ? 18f : 6f) * dt));
            float driftTarget = groundedNow && horizontal > 2f ? Mathf.Clamp(sideSpeed / 4f, -1f, 1f) : 0f;
            _drift = Mathf.Lerp(_drift, driftTarget, 1f - Mathf.Exp(-10f * dt));
            float scrape = Mathf.Max(_brake - 0.3f, Mathf.Abs(_drift) - 0.4f);
            if (scrape > 0f)
            {
                _dustAccumulator += dt * 40f * scrape;
                int n = (int)_dustAccumulator;
                if (n > 0) { _dustAccumulator -= n; EmitDust(n, transform.position + transform.forward * 0.15f, 0.7f); }
            }

            // Приземление и прыжок — для «мультяшного» сплющивания.
            if (wasAirborne && touching && _smoothVerticalSpeed < -3f)
            {
                float impact = Mathf.Clamp(-_smoothVerticalSpeed, 3f, 12f);
                _squashVelocity -= impact * 0.9f;
                // Удар лап о землю: пыль в стороны, у своего кота камера «приседает».
                EmitDust((int)(4 + impact * 1.2f), transform.position, 1.2f + impact * 0.12f);
                if (IsOwner) CameraKick(Vector3.down * impact * 0.09f, impact > 8f ? 0.15f : 0f);
            }

            // Шаг под настоящую скорость: клип шагает так же быстро, как кот едет, — лапы не «плывут».
            if (_hasStride)
            {
                float target = 1f;
                if (_animGrounded && _smoothSpeed > 0.5f && _sink != null)
                {
                    float natural = _sink.AnimSpeed / Mathf.Max(0.05f, _strideMul);
                    if (natural > 0.3f) target = Mathf.Clamp(_smoothSpeed / natural, 0.75f, 1.6f);
                }
                _strideMul = Mathf.Lerp(_strideMul, target, 1f - Mathf.Exp(-3f * dt));
                animator.SetFloat(StrideMulId, _strideMul);
            }

            // Разгон: толчок задними лапами — наклон вперёд, пыль из-под лап.
            float pushOffTarget = groundedNowForAccel(touching) && _smoothForwardAccel > 5f ? Mathf.Clamp01((_smoothForwardAccel - 5f) / 14f) : 0f;
            if (pushOffTarget > 0.5f && _pushOff < 0.25f && _smoothSpeed < 3f) EmitDust(4, transform.position - transform.forward * 0.25f, 1.1f);
            _pushOff = Mathf.Lerp(_pushOff, pushOffTarget, 1f - Mathf.Exp(-(pushOffTarget > _pushOff ? 20f : 6f) * dt));

            UpdateBump(dt);

            UpdateIdle(dt, touching, knocked, crouch);

            if (_hasTurnParam) animator.SetFloat(TurnId, _smoothTurn);
            if (_hasCrouchParam) animator.SetBool(CrouchId, crouch);
            animator.SetFloat(SpeedId, _smoothSpeed);
            animator.SetFloat(VerticalSpeedId, _smoothVerticalSpeed);
            animator.SetBool(GroundedId, _animGrounded);
            animator.SetBool(KnockedId, knocked);
            if (knocked) animator.ResetTrigger(JumpId); // не «прыгать» сразу после того, как встали

            UpdateActionLayer(dt);
        }

        /// <summary>Слой лапы/удара включается только пока идёт действие — бег под ним не прерывается.</summary>
        private void UpdateActionLayer(float dt)
        {
            if (animator.layerCount <= ActionLayer) return;

            bool active = animator.GetCurrentAnimatorStateInfo(ActionLayer).IsTag("Action")
                          || animator.GetNextAnimatorStateInfo(ActionLayer).IsTag("Action");
            float target = active ? 1f : 0f;
            // Включаем быстро (лапа должна успеть к удару), выключаем мягко.
            float sharpness = active ? 30f : 8f;
            _actionWeight = Mathf.Lerp(_actionWeight, target, 1f - Mathf.Exp(-sharpness * dt));
            animator.SetLayerWeight(ActionLayer, _actionWeight);

            if (animator.layerCount > FullActionLayer)
            {
                bool fullActive = animator.GetCurrentAnimatorStateInfo(FullActionLayer).IsTag("Action")
                                  || animator.GetNextAnimatorStateInfo(FullActionLayer).IsTag("Action");
                // Удар с места, а кот сорвался в бег — всё тело отдаём бегу, лапа доигрывает только слоем «перед».
                if (fullActive && _smoothSpeed > 2f) fullActive = false;
                float fullTarget = fullActive ? 1f : 0f;
                _fullActionWeight = Mathf.Lerp(_fullActionWeight, fullTarget, 1f - Mathf.Exp(-(fullActive ? 30f : 7f) * dt));
                animator.SetLayerWeight(FullActionLayer, _fullActionWeight);
            }
        }

        // ───────────────────────── Безделье ─────────────────────────

        /// <summary>
        /// Стоит без дела — оглядывается, зевает, тянется; потом садится, потом ложится.
        /// Любое движение — сразу встаёт. Считается у каждого игрока сам по себе (это только вид).
        /// </summary>
        private void UpdateIdle(float dt, bool grounded, bool knocked, bool crouch)
        {
            if (!_hasIdleParams) return;

            bool idle = _smoothSpeed < 0.15f && grounded && !knocked && !crouch && Mathf.Abs(_smoothVerticalSpeed) < 0.5f;
            if (!idle)
            {
                _idleTime = 0f;
                _nextIdleAction = idleActionDelay + (float)_rng.NextDouble() * 2f;
            }
            else
            {
                _idleTime += dt;
            }

            int rest = _idleTime > lieDelay ? 2 : _idleTime > sitDelay ? 1 : 0;
            if (rest != _rest)
            {
                _rest = rest;
                animator.SetInteger(RestId, rest);
            }

            if (idle && rest == 0 && _idleTime > _nextIdleAction)
            {
                animator.SetInteger(IdleVariantId, _rng.Next(5));
                animator.SetTrigger(IdleActionId);
                _nextIdleAction = _idleTime + 5f + (float)_rng.NextDouble() * 4f;
            }
        }

        // ───────────────────────── Процедурная живость ─────────────────────────

        /// <summary>Наклон в поворотах, наклон по земле, сплющивание/вытягивание (пружина).</summary>
        private void ApplyBodyMotion()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed01 = Mathf.Clamp01(_smoothSpeed / 6f);
            float targetLean = -_smoothTurn * maxLean * speed01;
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-8f * dt));

            // Наклон по земле: только когда стоим на ней.
            Quaternion targetSlope = Quaternion.identity;
            if (_animGrounded && _groundNormal.y > 0.6f)
            {
                Vector3 localNormal = Quaternion.Inverse(transform.rotation) * _groundNormal;
                targetSlope = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, localNormal), slopeAlign);
            }
            _slope = Quaternion.Slerp(_slope, targetSlope, 1f - Mathf.Exp(-10f * dt));

            // Пружина сплющивания: s > 0 — вытянут, s < 0 — сплющен.
            _squashVelocity += (-160f * _squash - 14f * _squashVelocity) * dt;
            _squash = Mathf.Clamp(_squash + _squashVelocity * dt, -1f, 1f);
            float s = _squash * squashAmount;

            // Поворот на месте: лёгкое покачивание шагов поверх клипа — кот переступает, а не крутится статуей.
            _stepPhase += dt * 14f * _turnInPlace;
            float stepBob = Mathf.Abs(Mathf.Sin(_stepPhase)) * 0.025f * _turnInPlace;
            float stepRoll = Mathf.Sin(_stepPhase) * 4f * _turnInPlace;
            model.localPosition = _modelLocalPosition + Vector3.up * stepBob;
            StrikePose(dt, out Vector3 strikeOffset, out Quaternion strikeRotation, out float strikeSquash);
            s += strikeSquash * 0.08f;
            model.localPosition += strikeOffset;
            // Пыль в момент удара лапой.
            if (_strikeDustAt >= 0f && _strikeT >= _strikeDustAt)
            {
                _strikeDustAt = -1f;
                EmitDust(3, transform.position + transform.forward * 0.45f, 0.8f);
            }
            // Толкнулся о другого кота: корпус отпружинивает в сторону.
            _bumpVelocity += (-160f * _bump - 13f * _bumpVelocity) * dt;
            _bump += _bumpVelocity * dt;
            Vector3 localBump = transform.InverseTransformDirection(_bump);
            model.localPosition += new Vector3(localBump.x, 0f, localBump.z) * 0.6f;

            // Торможение: откинулся назад (нос вверх), занос: завалился в сторону скольжения; разгон — нос вниз.
            float pitch = -_brake * brakePitch + _pushOff * pushOffPitch - localBump.z * 30f;
            float roll = _lean + _drift * skidRoll + stepRoll - localBump.x * 40f; // упирается против скольжения
            model.localRotation = _slope * strikeRotation * _modelLocalRotation * Quaternion.Euler(pitch, 0f, roll);
            s -= _brake * 0.06f; // чуть присел на задние лапы
            model.localScale = new Vector3(_modelScale.x * (1f - s * 0.5f), _modelScale.y * (1f + s), _modelScale.z * (1f - s * 0.5f));
        }

        /// <summary>
        /// Голова смотрит туда же, куда камера (свой кот) или на ближайшего кота (чужие), хвост гуляет в поворотах.
        /// Делается после аниматора — поверх клипа.
        /// </summary>
        private void ApplyLookAndTail()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            bool knocked = _controller != null && _controller.State.Value != CatState.Normal;

            if (_head != null) _head.localScale = Vector3.one * headScale;

            if (_head != null && !knocked)
            {
                Vector3 desired = transform.forward;
                if (IsOwner)
                {
                    if (_camera == null) _camera = Camera.main != null ? Camera.main.GetComponent<CatCamera>() : null;
                    if (_camera != null) desired = _camera.LookDirection;
                }
                else
                {
                    var nearest = NearestCat(5f);
                    if (nearest != null) desired = (nearest.position + Vector3.up * 0.5f) - (_head.position);
                }

                Vector3 flatBody = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 flatDesired = Vector3.ProjectOnPlane(desired, Vector3.up);
                float yawAngle = flatDesired.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(flatBody, flatDesired, Vector3.up) : 0f;
                // Смотрим назад через плечо только до предела; дальше голова возвращается вперёд.
                if (Mathf.Abs(yawAngle) > 120f) yawAngle = 0f;
                // Голова «ведёт» поворот: сначала смотрит, куда разворачивается, потом тело догоняет.
                yawAngle += _smoothTurn * 35f;
                yawAngle = Mathf.Clamp(yawAngle, -70f, 70f);
                float pitchAngle = Mathf.Asin(Mathf.Clamp(desired.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (IsOwner) pitchAngle += 18f; // камера обычно смотрит чуть сверху — это «прямо» для кота
                pitchAngle = Mathf.Clamp(pitchAngle, -35f, 30f);

                float weight = headLookWeight * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(_smoothSpeed / 6f));
                Quaternion target = Quaternion.AngleAxis(yawAngle * weight, Vector3.up) * Quaternion.AngleAxis(-pitchAngle * weight, transform.right);
                _look = Quaternion.Slerp(_look, target, 1f - Mathf.Exp(-6f * dt));

                if (_neck != null)
                {
                    _neck.rotation = Quaternion.Slerp(Quaternion.identity, _look, 0.45f) * _neck.rotation;
                    _head.rotation = Quaternion.Slerp(Quaternion.identity, _look, 0.55f) * _head.rotation;
                }
                else
                {
                    _head.rotation = _look * _head.rotation;
                }
            }

            // Хвост: уводит в сторону поворота, в покое лениво покачивается.
            float time = Time.time;
            for (int i = 0; i < _tail.Count; i++)
            {
                float k = (i + 1f) / _tail.Count;
                float angle = -_smoothTurn * tailSway * k + Mathf.Sin(time * 1.6f + i * 0.7f) * (2.5f + 2f * k);
                _tail[i].rotation = Quaternion.AngleAxis(angle, transform.up) * _tail[i].rotation;
            }
        }

        private Transform NearestCat(float radius)
        {
            Transform best = null;
            float bestDistance = radius * radius;
            foreach (var cat in CatController.All)
            {
                if (cat == null || cat == _controller) continue;
                float d = (cat.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = cat.transform;
                }
            }
            return best;
        }

        /// <summary>Короткий «вдох» корпусом — кот мяукнул/спел ноту.</summary>
        public void Pulse() => _squashVelocity += 4.5f;

        private bool CheckGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 0.3f + groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var body = Hits[i].collider.attachedRigidbody;
                if (body == null || body.gameObject != gameObject)
                {
                    _groundNormal = Hits[i].normal;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Удар лапой: правая и левая по очереди. С места — всем телом (упор задними лапами, разворот корпуса),
        /// на бегу — передом поверх бега, резче. Поверх клипа — процедурный выпад: замах назад, бросок вперёд, возврат.
        /// </summary>
        private void OnPush()
        {
            if (animator == null) return;
            _pushSide ^= 1;
            _strikeMoving = _smoothSpeed > 1.2f;
            _strikeT = 0f;
            _strikeDustAt = 0.13f;
            animator.SetInteger(PushSideId, _pushSide);
            if (_hasStandPush && !_strikeMoving)
            {
                animator.SetTrigger(PushStandId);
                _fullActionWeight = Mathf.Max(_fullActionWeight, 0.6f);
            }
            else
            {
                animator.SetTrigger(PushId);
                _actionWeight = Mathf.Max(_actionWeight, 0.6f); // сразу видно замах
            }
        }

        /// <summary>Выпад корпусом: смещение вперёд (м), наклон носом вниз (°), поворот к лапе (°), сплющивание.</summary>
        private void StrikePose(float dt, out Vector3 offset, out Quaternion rotation, out float squash)
        {
            offset = Vector3.zero;
            rotation = Quaternion.identity;
            squash = 0f;
            if (_strikeT < 0f) return;
            _strikeT += dt;
            float t = _strikeT;
            const float impact = 0.12f; // = CatController.pushImpactDelay
            float reach = _strikeMoving ? 0.16f : 0.11f;
            float forward, pitch;
            if (t < impact)
            {
                // Замах: чуть назад и присел на задние лапы.
                float k = t / impact;
                forward = -0.04f * Mathf.Sin(k * Mathf.PI * 0.5f);
                pitch = -4f * k;
                squash = -0.35f * k;
            }
            else if (t < impact + 0.08f)
            {
                // Бросок: резко вперёд, нос вниз, вытянулся.
                float k = (t - impact) / 0.08f;
                forward = Mathf.Lerp(-0.04f, reach, 1f - (1f - k) * (1f - k));
                pitch = Mathf.Lerp(-4f, 9f, k);
                squash = Mathf.Lerp(-0.35f, 0.5f, k);
            }
            else
            {
                // Возврат пружиной.
                float k = Mathf.Clamp01((t - impact - 0.08f) / 0.32f);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                forward = Mathf.Lerp(reach, 0f, ease) + Mathf.Sin(k * Mathf.PI) * 0.015f;
                pitch = Mathf.Lerp(9f, 0f, ease);
                squash = Mathf.Lerp(0.5f, 0f, ease);
                if (k >= 1f) _strikeT = -1f;
            }
            float twist = (_pushSide == 0 ? -1f : 1f) * (_strikeMoving ? 4f : 9f) * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI);
            offset = Vector3.forward * forward;
            rotation = Quaternion.Euler(pitch, twist, 0f);
        }

        private void OnHit()
        {
            if (animator != null) animator.SetTrigger(HitId);
            // По тебе попали: стоп-кадр, сплющило, пыль, у своего кота — тряска камеры.
            _hitStopUntil = Time.time + 0.08f;
            _squashVelocity -= 5f;
            EmitDust(6, transform.position, 1.3f);
            if (IsOwner) CameraKick(-transform.forward * 0.6f, 0.45f);
        }

        /// <summary>Своя лапа попала по коту/предмету: короткий стоп-кадр и лёгкая тряска.</summary>
        private void OnPushLanded()
        {
            _hitStopUntil = Time.time + 0.07f; // момент касания лапой
            if (IsOwner) CameraKick(transform.forward * 0.4f, 0.22f);
        }

        private static bool groundedNowForAccel(bool touching) => touching;

        // ───────────────────────── Пыль и камера ─────────────────────────

        private void CreateDust()
        {
            if (dustMaterial == null || _dust != null) return;
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            _dust = go.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startColor = dustColor;
            main.gravityModifier = -0.04f;
            main.maxParticles = 120;
            var emission = _dust.emission;
            emission.rateOverTime = 0f;
            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;
            var size = _dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            var color = _dust.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _dust.Play();
        }

        private void EmitDust(int count, Vector3 at, float speedScale)
        {
            if (_dust == null || count <= 0 || !_animGrounded) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < count; i++)
            {
                p.position = at + new Vector3(Random.Range(-0.15f, 0.15f), 0.04f, Random.Range(-0.15f, 0.15f));
                Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 0.7f), Random.Range(-1f, 1f)).normalized;
                p.velocity = dir * Random.Range(0.4f, 1.1f) * speedScale;
                _dust.Emit(p, 1);
            }
        }

        private static void CameraKick(Vector3 kick, float shake)
        {
            var camera = Camera.main != null ? Camera.main.GetComponent<CatCamera>() : null;
            if (camera == null) return;
            camera.Kick(kick);
            if (shake > 0f) camera.Shake(shake);
        }

        /// <summary>Коты столкнулись (у всех игроков считается одинаково — по сближению): корпус отпружинивает, пыль, глухой стук.</summary>
        private void UpdateBump(float dt)
        {
            CatController nearest = null;
            float best = 0.85f;
            foreach (var cat in CatController.All)
            {
                if (cat == null || cat == _controller) continue;
                float d = Vector3.Distance(cat.transform.position, transform.position);
                if (d < best) { best = d; nearest = cat; }
            }
            if (nearest == null) { _lastBumpDistance = 99f; return; }
            float closing = (_lastBumpDistance - best) / Mathf.Max(dt, 0.0001f);
            _lastBumpDistance = best;
            if (closing < 1.8f || Time.time < _nextBumpTime) return;
            _nextBumpTime = Time.time + 0.35f;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - nearest.transform.position, Vector3.up).normalized;
            _bumpVelocity += away * Mathf.Clamp(closing * 0.35f, 0.6f, 2.2f);
            _squashVelocity -= 2.5f;
            EmitDust(3, (transform.position + nearest.transform.position) * 0.5f, 0.9f);
            Maoshi.Audio.AudioManager.PlayAt("push", transform.position + Vector3.up * 0.4f, 0.35f, 0.1f, 18f);
            if (IsOwner) CameraKick(away * 0.15f, 0.1f);
        }
    }
}
