using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Maoshi.Cat
{
    /// <summary>Состояние кота.</summary>
    public enum CatState : byte
    {
        /// <summary>Бегает и слушается игрока.</summary>
        Normal,
        /// <summary>Сбит с лап: кувыркается как тряпичный, управление отключено.</summary>
        Knocked,
        /// <summary>Поднимается после падения.</summary>
        GettingUp
    }

    /// <summary>
    /// Общий контроллер кота для всех сцен: бег, прыжок, толчок, падение, возврат на карту.
    ///
    /// Сетевая модель (решение этапа 0):
    /// - Физику своего кота считает ВЛАДЕЛЕЦ: ввод срабатывает мгновенно, без пинга.
    ///   NetworkTransform (Owner) рассылает позицию, у остальных кот кинематический и просто повторяет её.
    /// - Толчок идёт через СЕРВЕР: владелец говорит «толкаю вот этого», сервер проверяет дистанцию и кулдаун
    ///   и отправляет удар владельцу жертвы. Так никто не может толкать через всю карту.
    /// - Ящики и прочие предметы принадлежат серверу; клиенты двигают их через сервер.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CatController : NetworkBehaviour
    {
        [Header("Бег")]
        [SerializeField] private float moveSpeed = 6f;
        [Tooltip("Разгон, м/с². Кот разгоняется за ~0.2 с — есть ощущение веса, но управление отзывчивое.")]
        [SerializeField] private float acceleration = 32f;
        [Tooltip("Торможение, когда отпустили кнопки.")]
        [SerializeField] private float deceleration = 26f;
        [Tooltip("Торможение при резком развороте на скорости (кот «буксует» лапами).")]
        [SerializeField] private float skidDeceleration = 55f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.45f;
        [Tooltip("Плавность поворота: чем больше, тем быстрее кот разворачивается к направлению бега.")]
        [SerializeField] private float turnSharpness = 9f;
        [Tooltip("Скорость поворота на месте и на полном бегу, градусов в секунду: на скорости кот бежит дугой.")]
        [SerializeField] private float maxTurnSpeedSlow = 720f;
        [SerializeField] private float maxTurnSpeedFast = 330f;

        [Header("Инерция (этап 24)")]
        [Tooltip("Торможение после бега, когда отпустили кнопки (м/с²): кот проезжает немного вперёд. На малой скорости — deceleration.")]
        [SerializeField] private float brakeDecelerationFast = 13f;
        [Tooltip("На полной скорости кот бежит туда, куда смотрит: насколько градусов скорость может расходиться с корпусом.")]
        [SerializeField] private float maxHeadingOffsetFast = 30f;
        [Tooltip("Разворот в заносе: как быстро кот поворачивается, пока тормозит лапами (°/с).")]
        [SerializeField] private float skidTurnSpeed = 620f;
        [Tooltip("Резкий поворот на бегу (больше 70°) немного гасит скорость — кот «закладывает» вираж.")]
        [SerializeField] private float cornerDrag = 9f;
        [Tooltip("Разворот с места: если новое направление дальше этого угла, кот сначала поворачивается, переступая лапами, и только потом идёт.")]
        [SerializeField] private float pivotAngle = 55f;
        [Tooltip("Скорость разворота с места (°/с) — ровная, чтобы успевала проиграться анимация поворота.")]
        [SerializeField] private float pivotTurnSpeed = 540f;

        [Header("Крадётся (Ctrl / C)")]
        [SerializeField] private float crouchSpeed = 2.2f;

        [Header("Прыжок")]
        [SerializeField] private float jumpVelocity = 7.5f;
        [SerializeField] private float extraGravity = 14f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float groundCheckRadius = 0.28f;

        [Header("Толчок")]
        [SerializeField] private float pushRange = 1.1f;
        [SerializeField] private float pushRadius = 0.7f;
        [SerializeField] private float pushForce = 3.5f;
        [SerializeField] private float pushUpForce = 1f;
        [SerializeField] private float pushCooldown = 0.6f;
        [SerializeField] private float pushLunge = 1.5f;
        [Tooltip("Задержка удара после нажатия — чтобы толчок совпал с моментом, когда лапа долетает.")]
        [SerializeField] private float pushImpactDelay = 0.12f;
        [Tooltip("После толчка кот «шатается»: управление ослаблено, чтобы толчок успел сдвинуть его.")]
        [SerializeField] private float staggerDuration = 0.35f;
        [SerializeField, Range(0f, 1f)] private float staggerControl = 0.15f;
        [Tooltip("Запас дистанции для проверки на сервере: позиция клиента приходит с задержкой.")]
        [SerializeField] private float serverRangeTolerance = 1.5f;
        [Tooltip("Сила толчка предметов (импульс, учитывает массу: тяжёлое летит меньше).")]
        [SerializeField] private float propPushMultiplier = 0.5f;

        [Header("Падение")]
        [SerializeField] private float knockdownDuration = 1f;
        [Tooltip("Неуязвимость к ударам после оглушения, с.")]
        [SerializeField] private float hitImmunityAfterKnock = 0.6f;
        [Tooltip("Быстрее этого кот от удара не отлетает (по горизонтали), м/с.")]
        [SerializeField] private float maxHitSpeed = 5f;
        [SerializeField] private float getUpDuration = 0.35f;
        [Tooltip("Удар о землю с такой скоростью сбивает кота с лап.")]
        [SerializeField] private float hardLandingSpeed = 16f;

        [Header("Нельзя стоять на других котах")]
        [Tooltip("С какой силой кот соскальзывает с головы другого кота.")]
        [SerializeField] private float slideOffAcceleration = 45f;
        [Tooltip("Если коты пересеклись (прыжок сверху, задержка сети) — с какой скоростью расталкиваются, м/с.")]
        [SerializeField] private float maxSeparationSpeed = 4f;

        [Header("Мир")]
        [Tooltip("Ниже этой высоты кот считается упавшим с карты и возвращается на старт.")]
        [SerializeField] private float killY = -15f;
        [SerializeField] private float propNudgeInterval = 0.1f;

        private static readonly List<CatController> AllCats = new();

        /// <summary>Все коты в текущей сцене.</summary>
        public static IReadOnlyList<CatController> All => AllCats;

        /// <summary>
        /// Локальная блокировка управления (отсчёт перед мини-игрой, финиш).
        /// Физика продолжает работать, просто ввод игнорируется.
        /// </summary>
        public static bool ControlsLocked { get; set; }

        /// <summary>Только у владельца: множитель скорости (тяжёлая ноша в зубах и т.п.). Ставит мини-игра.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AllCats.Clear();
            ControlsLocked = false;
        }

        /// <summary>Найти кота игрока. null, если у игрока нет кота в этой сцене.</summary>
        public static CatController FindByOwner(ulong clientId)
        {
            // Чистим записи уничтоженных котов (на случай, если сцена выгрузилась без OnNetworkDespawn).
            AllCats.RemoveAll(cat => cat == null);

            foreach (var cat in AllCats)
            {
                if (cat.IsSpawned && cat.OwnerClientId == clientId) return cat;
            }

            return null;
        }

        /// <summary>Кот сделал толчок (у всех игроков — для анимации лапой).</summary>
        public event Action PushPerformed;

        /// <summary>Кота ударили (у всех игроков — для анимации удара).</summary>
        public event Action HitReceived;

        /// <summary>Только у владельца: лапа попала по кому-то (для «стоп-кадра» и тряски камеры).</summary>
        public event Action PushLanded;

        /// <summary>Кот крадётся — видно всем (для анимации). Пишет только владелец.</summary>
        public readonly NetworkVariable<bool> Crouching = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        /// <summary>Только у владельца: кот буксует при развороте (для пыли/анимации).</summary>
        public bool Skidding { get; private set; }

        /// <summary>Состояние видно всем (для эффектов). Пишет только владелец.</summary>
        public readonly NetworkVariable<CatState> State = new(
            CatState.Normal,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private Rigidbody _rb;
        private NetworkTransform _netTransform;
        private CatInput _input;
        private CatCamera _camera;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private float _lastGroundedTime = -1f;
        private bool _jumpQueued;
        private bool _pushQueued;
        private float _nextPushTime;
        private float _stateTimer;
        private float _nextPropNudgeTime;
        private Quaternion _getUpStartRotation;
        private float _staggerUntil;
        private float _hitImmuneUntil;
        private Quaternion _pivotLook = Quaternion.identity;
        private bool _pivotActive;
        private float _pendingPushAt = -1f;
        private Collider _collider;

        // Только на сервере.
        private float _serverNextPushTime;

        private static readonly Collider[] OverlapBuffer = new Collider[32];

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _netTransform = GetComponent<NetworkTransform>();
            _collider = GetComponent<Collider>();
        }

        // ───────────────────────── Сеть ─────────────────────────

        public override void OnNetworkSpawn()
        {
            AllCats.Add(this);

            // Звёздочки над головой оглушённого кота — у всех.
            if (GetComponent<StunStars>() == null) gameObject.AddComponent<StunStars>();
            // Статистика партии: сколько раз оглушён (номинация «Звёздочки над головой»).
            if (IsServer) State.OnValueChanged += CountStun;

            if (!IsOwner) return;

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            _input = new CatInput();
            _input.Enable();

            _camera = CatCamera.AttachToMain();
            if (_camera != null) _camera.SetTarget(transform);
        }

        private void CountStun(CatState previous, CatState next)
        {
            if (next == CatState.Knocked && previous == CatState.Normal) Maoshi.Session.SessionStats.Add(OwnerClientId, Maoshi.Session.Stat.Stunned);
        }

        public override void OnDestroy()
        {
            AllCats.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkDespawn()
        {
            AllCats.Remove(this);
            State.OnValueChanged -= CountStun;

            if (_input != null)
            {
                _input.Dispose();
                _input = null;
            }

            if (_camera != null) _camera.ClearTarget(transform);
        }

        // ───────────────────────── Цикл владельца ─────────────────────────

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            // Нажатия ловим в Update (каждый кадр), а применяем в FixedUpdate (шаг физики).
            if (_input.JumpPressed) _jumpQueued = true;
            // Только в редакторе: F7 — оглушить своего кота (проверить звёздочки).
            if (Application.isEditor && Maoshi.GameKeyboard.Current != null && Maoshi.GameKeyboard.Current.f7Key.wasPressedThisFrame)
                ReceiveHitRpc(Vector3.up * 2f, true);
            if (_input.PushPressed) _pushQueued = true;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsOwner) return;

            if (transform.position.y < killY)
            {
                Respawn();
                return;
            }

            SeparateFromCats();

            switch (State.Value)
            {
                case CatState.Normal:
                    TickNormal();
                    break;
                case CatState.Knocked:
                    TickKnocked();
                    break;
                case CatState.GettingUp:
                    TickGettingUp();
                    break;
            }

            _jumpQueued = false;
            _pushQueued = false;
        }

        private void TickNormal()
        {
            bool grounded = IsGrounded(out CatController catBelow);
            if (grounded) _lastGroundedTime = Time.time;

            // Стоять на другом коте нельзя: опоры нет, прыгнуть нельзя, кот соскальзывает в сторону.
            if (!grounded && catBelow != null) SlideOff(catBelow);

            if (ControlsLocked)
            {
                _jumpQueued = false;
                _pushQueued = false;
            }

            // Направление относительно камеры: «вперёд» — туда, куда смотрит камера.
            Vector2 move = ControlsLocked ? Vector2.zero : _input.Move;
            Vector3 forward = _camera != null ? _camera.PlanarForward : Vector3.forward;
            Vector3 right = _camera != null ? _camera.PlanarRight : Vector3.right;
            Vector3 direction = forward * move.y + right * move.x;

            bool crouch = !ControlsLocked && grounded && _input.CrouchHeld;
            if (Crouching.Value != crouch) Crouching.Value = crouch;

            // Разгон / торможение / занос. В воздухе управление слабее.
            Vector3 velocity = _rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float speed = horizontal.magnitude;
            Vector3 targetHorizontal = direction * (crouch ? crouchSpeed : moveSpeed) * SpeedMultiplier;

            bool hasInput = direction.sqrMagnitude > 0.01f;
            float speed01 = Mathf.Clamp01(speed / moveSpeed);
            Vector3 facing = Vector3.ProjectOnPlane(_rb.rotation * Vector3.forward, Vector3.up).normalized;

            // Отпустили кнопки после бега — кот не встаёт как вкопанный, а проезжает и тормозит лапами.
            float rate = hasInput ? acceleration : Mathf.Lerp(deceleration, brakeDecelerationFast, speed01 * speed01);
            // Резкий разворот на скорости: тормозим юзом и одновременно разворачиваемся корпусом.
            Skidding = grounded && speed > 3f && direction.sqrMagnitude > 0.25f && Vector3.Dot(horizontal / speed, direction.normalized) < -0.2f;
            if (Skidding)
            {
                rate = skidDeceleration;
                targetHorizontal = Vector3.zero;
            }
            // Разворот с места: сперва повернуться (с анимацией переступания), потом идти.
            bool pivoting = !Skidding && hasInput && grounded && speed < 1.5f && Vector3.Angle(facing, direction) > pivotAngle;
            if (pivoting)
            {
                targetHorizontal = Vector3.zero;
            }
            else if (hasInput && grounded && speed > 2f && targetHorizontal.sqrMagnitude > 0.01f)
            {
                // На бегу кот бежит туда, куда смотрит: скорость тянется за корпусом — получаются дуги, а не «скольжение боком».
                float maxOffset = Mathf.Lerp(180f, maxHeadingOffsetFast, speed01) * Mathf.Deg2Rad;
                Vector3 steered = Vector3.RotateTowards(facing, direction.normalized, maxOffset, 0f);
                // Крутой вираж (больше 70°) гасит часть скорости.
                float corner = Vector3.Angle(facing, direction);
                float keep = corner > 70f ? Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(70f, 140f, corner)) : 1f;
                targetHorizontal = steered * targetHorizontal.magnitude * keep;
                if (corner > 70f) rate = Mathf.Max(rate, cornerDrag + acceleration * 0.5f);
            }

            float control = grounded ? 1f : airControl;
            if (Time.time < _staggerUntil) control *= staggerControl;
            Vector3 change = Vector3.ClampMagnitude(targetHorizontal - horizontal, rate * control * Time.fixedDeltaTime);
            _rb.AddForce(change, ForceMode.VelocityChange);

            if (hasInput)
            {
                Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                if (pivoting)
                {
                    _pivotLook = look;
                    _pivotActive = true;
                    _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, look, pivotTurnSpeed * Time.fixedDeltaTime));
                }
                else if (Skidding)
                {
                    // Разворот юзом: корпус быстро поворачивается, пока лапы гасят скорость.
                    _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, look, skidTurnSpeed * Time.fixedDeltaTime));
                }
                else
                {
                    // Поворот по дуге: на месте — быстро (с переступанием лапами), на полном бегу — плавной дугой.
                    float maxTurn = Mathf.Lerp(maxTurnSpeedSlow, maxTurnSpeedFast, speed01);
                    float t = 1f - Mathf.Exp(-turnSharpness * Time.fixedDeltaTime);
                    Quaternion smoothed = Quaternion.Slerp(_rb.rotation, look, t);
                    _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, smoothed, maxTurn * Time.fixedDeltaTime));
                }
            }

            // Коротко нажал кнопку — кот всё равно доворачивается до конца (не застывает на полпути).
            if (hasInput && !pivoting) _pivotActive = false;
            if (!hasInput && _pivotActive)
            {
                if (!grounded || speed > 1.5f || Quaternion.Angle(_rb.rotation, _pivotLook) < 2f) _pivotActive = false;
                else _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, _pivotLook, pivotTurnSpeed * Time.fixedDeltaTime));
            }

            // «Время койота»: можно прыгнуть чуть позже, чем сошёл с края, — так приятнее.
            if (_jumpQueued && Time.time - _lastGroundedTime <= coyoteTime)
            {
                velocity = _rb.linearVelocity;
                velocity.y = jumpVelocity;
                _rb.linearVelocity = velocity;
                _lastGroundedTime = -1f;
            }

            if (!grounded) _rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

            if (_pushQueued && Time.time >= _nextPushTime) StartPush();
            if (_pendingPushAt > 0f && Time.time >= _pendingPushAt)
            {
                _pendingPushAt = -1f;
                TryPush();
            }
        }

        private void TickKnocked()
        {
            _rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

            // Оглушённый кот не скользит по земле: гасим горизонтальную скорость.
            Vector3 velocity = _rb.linearVelocity;
            float damping = Mathf.Exp(-5f * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(velocity.x * damping, velocity.y, velocity.z * damping);

            _stateTimer -= Time.fixedDeltaTime;
            // Встаём, когда время вышло и кот уже не летит. Если застрял в воздухе надолго — всё равно встаём.
            bool settled = _rb.linearVelocity.sqrMagnitude < 4f;
            if (_stateTimer <= 0f && (settled || _stateTimer < -2f))
            {
                _stateTimer = getUpDuration;
                _getUpStartRotation = _rb.rotation;
                _rb.angularVelocity = Vector3.zero;
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
                State.Value = CatState.GettingUp;
            }
        }

        private void TickGettingUp()
        {
            _stateTimer -= Time.fixedDeltaTime;
            float t = 1f - Mathf.Clamp01(_stateTimer / getUpDuration);

            // Поворачиваемся «на лапы», сохраняя направление взгляда.
            Vector3 flatForward = Vector3.ProjectOnPlane(_getUpStartRotation * Vector3.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(_getUpStartRotation * Vector3.up, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

            _rb.MoveRotation(Quaternion.Slerp(_getUpStartRotation, upright, t));

            if (_stateTimer <= 0f)
            {
                State.Value = CatState.Normal;
                _hitImmuneUntil = Time.time + hitImmunityAfterKnock;
            }
        }

        // ───────────────────────── Толчок ─────────────────────────

        /// <summary>Нажали «толкнуть»: сразу замах лапой (анимация у всех), сам удар — через pushImpactDelay.</summary>
        private void StartPush()
        {
            _nextPushTime = Time.time + pushCooldown;
            _pendingPushAt = Time.time + pushImpactDelay;

            PushPerformed?.Invoke();
            PushAnimRpc();
        }

        /// <summary>Момент касания лапой: выпад и поиск цели.</summary>
        private void TryPush()
        {
            // Небольшой выпад вперёд — толчок чувствуется даже при промахе.
            _rb.AddForce(transform.forward * pushLunge, ForceMode.VelocityChange);

            Vector3 center = transform.position + Vector3.up * 0.45f + transform.forward * pushRange;
            int count = Physics.OverlapSphereNonAlloc(center, pushRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);

            NetworkObject best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var body = OverlapBuffer[i].attachedRigidbody;
                if (body == null || body == _rb) continue;
                if (!body.TryGetComponent(out NetworkObject target) || !target.IsSpawned) continue;

                float distance = (body.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = target;
                }
            }

            if (best != null)
            {
                PushRpc(best);
                PushLanded?.Invoke();
            }
        }

        /// <summary>Сервер: кот толкнул лапой другого кота (кто, кого). Для правил мини-игр (передать пирожок и т.п.).</summary>
        public static event System.Action<CatController, CatController> ServerCatPushed;

        /// <summary>Владелец просит сервер толкнуть цель. Сервер проверяет, что это честно.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PushRpc(NetworkObjectReference targetReference)
        {
            if (Time.time < _serverNextPushTime) return;
            if (!targetReference.TryGet(out NetworkObject target) || target == NetworkObject) return;

            Vector3 offset = target.transform.position - transform.position;
            if (offset.magnitude > pushRange + pushRadius + serverRangeTolerance) return;

            // Небольшой запас к кулдауну: сообщения клиента приходят неравномерно.
            _serverNextPushTime = Time.time + pushCooldown * 0.8f;

            Vector3 direction = Vector3.ProjectOnPlane(offset, Vector3.up);
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            Vector3 impulse = direction * pushForce + Vector3.up * pushUpForce;

            if (target.TryGetComponent(out CatController victim))
            {
                ServerCatPushed?.Invoke(this, victim);
                // Любой удар оглушает (решает владелец жертвы: у неё может быть неуязвимость после оглушения).
                if (victim.State.Value == CatState.Normal) Maoshi.Session.SessionStats.Add(OwnerClientId, Maoshi.Session.Stat.Hits);
                victim.ReceiveHitRpc(impulse, true);
            }
            else if (target.TryGetComponent(out Maoshi.Gameplay.IPushReceiver receiver))
            {
                // Особый предмет (клубок и т.п.) сам решает, как отлететь.
                receiver.OnPushedByCat(this, direction);
            }
            else if (target.TryGetComponent(out Rigidbody body) && !body.isKinematic)
            {
                // Предметы принадлежат серверу — двигаем прямо здесь. Импульс: лёгкое летит дальше тяжёлого.
                body.AddForce(impulse * propPushMultiplier, ForceMode.Impulse);
            }
        }

        /// <summary>Остальным игрокам: этот кот замахнулся лапой (только анимация).</summary>
        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        private void PushAnimRpc()
        {
            PushPerformed?.Invoke();
        }

        /// <summary>
        /// Сервер сообщает всем: этого кота ударили. Анимацию удара играют все,
        /// а физику (отлёт, шатание, падение) применяет только владелец.
        /// </summary>
        [Rpc(SendTo.Everyone)]
        public void ReceiveHitRpc(Vector3 impulse, bool knockDown)
        {
            // Оглушённого не бьём повторно: иначе серия ударов улетает на полкарты.
            if (State.Value != CatState.Normal) return;
            if (IsOwner && Time.time < _hitImmuneUntil) return;

            HitReceived?.Invoke();
            if (!IsOwner) return;

            _rb.AddForce(impulse, ForceMode.VelocityChange);
            ClampHorizontalSpeed(maxHitSpeed);

            _staggerUntil = Time.time + staggerDuration;

            // Один удар — оглушение (над головой кружатся звёздочки, StunStars). Параметр knockDown оставлен
            // для совместимости: раньше с лап сбивал только второй удар подряд.
            Knockdown();
        }

        private void ClampHorizontalSpeed(float max)
        {
            Vector3 velocity = _rb.linearVelocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude <= max * max) return;
            flat = flat.normalized * max;
            _rb.linearVelocity = new Vector3(flat.x, velocity.y, flat.z);
        }

        /// <summary>
        /// Сервер переносит кота в новую точку и делает её точкой возврата
        /// (например, выбывших — на скамейку зрителей).
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void SetRespawnAndTeleportRpc(Vector3 position, Quaternion rotation)
        {
            _spawnPosition = position;
            _spawnRotation = rotation;
            Respawn();
        }

        // ───────────────────────── Столкновения ─────────────────────────

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsSpawned || !IsOwner) return;

            // Жёсткое приземление с большой высоты — кот кувыркается.
            if (State.Value == CatState.Normal && collision.relativeVelocity.y > hardLandingSpeed)
                Knockdown();
        }

        private void OnCollisionStay(Collision collision)
        {
            // Хосту не нужно: на сервере его кот и так физически толкает предметы.
            if (!IsSpawned || !IsOwner || IsServer) return;
            if (Time.time < _nextPropNudgeTime) return;

            // У клиента предметы кинематические (ими управляет сервер), поэтому просим сервер подвинуть.
            var body = collision.rigidbody;
            if (body == null || !body.isKinematic) return;
            if (body.TryGetComponent(out CatController _)) return;
            if (!body.TryGetComponent(out NetworkObject prop) || !prop.IsSpawned) return;

            Vector3 velocity = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
            if (velocity.sqrMagnitude < 0.5f) return;

            _nextPropNudgeTime = Time.time + propNudgeInterval;
            NudgePropRpc(prop, velocity);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void NudgePropRpc(NetworkObjectReference propReference, Vector3 catVelocity)
        {
            if (!propReference.TryGet(out NetworkObject prop)) return;
            if (!prop.TryGetComponent(out Rigidbody body) || body.isKinematic) return;
            if ((prop.transform.position - transform.position).magnitude > 2.5f + serverRangeTolerance) return;

            // Ограничиваем скорость, которую мог прислать клиент.
            Vector3 velocity = Vector3.ClampMagnitude(catVelocity, moveSpeed);
            body.AddForce(velocity * (_rb.mass * propNudgeInterval * 5f), ForceMode.Impulse);
        }

        // ───────────────────────── Состояния ─────────────────────────

        /// <summary>
        /// Оглушить кота. Вызывается только у владельца.
        /// Кот не кувыркается (вращение заморожено), а стоит на месте и трясёт головой; потом короткая неуязвимость.
        /// </summary>
        private void Knockdown()
        {
            if (!IsOwner) return;

            _stateTimer = knockdownDuration;
            State.Value = CatState.Knocked;
        }

        /// <summary>Вернуть кота на точку появления (упал с карты).</summary>
        private void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            State.Value = CatState.Normal;

            Vector3 position = FindFreeSpot(_spawnPosition + Vector3.up * 0.5f);
            // Teleport — чтобы остальные не видели, как кот «пролетает» через карту.
            if (_netTransform != null)
                _netTransform.Teleport(position, _spawnRotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, _spawnRotation);
            _rb.position = position;
            _rb.rotation = _spawnRotation;
        }

        private static readonly Collider[] SpotBuffer = new Collider[16];

        /// <summary>
        /// Свободное место рядом с точкой: если там мебель, стена или другой кот — ищем выше и вокруг.
        /// Так кот не застревает в коллайдерах при появлении и возврате.
        /// </summary>
        private Vector3 FindFreeSpot(Vector3 point)
        {
            if (IsFree(point)) return point;
            for (int ring = 1; ring <= 3; ring++)
            {
                float radius = ring * 0.7f;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f + ring * 0.4f;
                    Vector3 candidate = point + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    if (IsFree(candidate)) return candidate;
                }
            }
            for (float up = 0.4f; up <= 3f; up += 0.4f)
                if (IsFree(point + Vector3.up * up)) return point + Vector3.up * up;
            return point;
        }

        private bool IsFree(Vector3 point)
        {
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * 0.25f, point + Vector3.up * 0.45f, 0.26f, SpotBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var body = SpotBuffer[i].attachedRigidbody;
                if (body == _rb) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Стоит ли кот на земле. Другие коты опорой не считаются: если под лапами только кот,
        /// возвращаем false, а в catBelow — того, на ком стоим.
        /// </summary>
        private bool IsGrounded(out CatController catBelow)
        {
            catBelow = null;
            Vector3 feet = transform.position + Vector3.up * (groundCheckRadius - 0.08f);
            int count = Physics.OverlapSphereNonAlloc(feet, groundCheckRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);

            bool ground = false;
            for (int i = 0; i < count; i++)
            {
                var body = OverlapBuffer[i].attachedRigidbody;
                if (body == _rb) continue;

                if (body != null && body.TryGetComponent(out CatController other))
                {
                    // Кот рядом на той же высоте — не «под нами». Под нами — если он заметно ниже.
                    if (other.transform.position.y < transform.position.y - 0.15f) catBelow = other;
                    continue;
                }

                ground = true;
            }

            return ground;
        }

        /// <summary>
        /// Если наш кот пересёкся с другим (прыгнул сверху, догнал с задержкой сети) — мягко расталкиваемся по горизонтали.
        /// Работает только у владельца: каждый выталкивает своего кота сам.
        /// </summary>
        private void SeparateFromCats()
        {
            if (_collider == null) return;

            foreach (var other in AllCats)
            {
                if (other == null || other == this || other._collider == null) continue;
                if ((other.transform.position - transform.position).sqrMagnitude > 4f) continue;

                if (!Physics.ComputePenetration(
                        _collider, _rb.position, _rb.rotation,
                        other._collider, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance) || distance < 0.01f)
                    continue;

                // Выталкиваем вбок, даже если пересеклись сверху — иначе кот «садится» на другого и застревает.
                Vector3 away = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (away.sqrMagnitude < 0.01f) away = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                if (away.sqrMagnitude < 0.0001f) away = transform.right;
                away.Normalize();

                float wanted = Mathf.Clamp(distance * 30f, 1f, maxSeparationSpeed);
                float current = Vector3.Dot(_rb.linearVelocity, away);
                if (current < wanted) _rb.AddForce(away * (wanted - current), ForceMode.VelocityChange);
            }
        }

        /// <summary>Сталкиваем своего кота с головы другого: в сторону от его центра.</summary>
        private void SlideOff(CatController catBelow)
        {
            Vector3 away = Vector3.ProjectOnPlane(transform.position - catBelow.transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.0025f) away = transform.right; // ровно сверху — в сторону
            _rb.AddForce(away.normalized * (slideOffAcceleration * Time.fixedDeltaTime), ForceMode.VelocityChange);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.45f + transform.forward * pushRange, pushRadius);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (groundCheckRadius - 0.08f), groundCheckRadius);
        }
    }
}
