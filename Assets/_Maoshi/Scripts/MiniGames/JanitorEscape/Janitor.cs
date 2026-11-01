using System;
using System.Collections.Generic;
using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Maoshi.MiniGames.JanitorEscape
{
    /// <summary>
    /// Дворник с метлой. Мозги — только на сервере (NavMeshAgent), позицию раздаёт NetworkTransform.
    /// Гоняется за ближайшим котом, рядом с целью делает рывок, замахивается (видно заранее!) и метёт:
    /// все коты перед ним в радиусе удара пойманы. Клиенты по состоянию State сами анимируют метлу и шаг.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Janitor : NetworkBehaviour
    {
        public enum JanitorState : byte
        {
            Idle,     // ждёт у дворницкой
            Chase,    // бежит за целью
            Windup,   // замах — у котов есть время отскочить
            Recover,  // промахнулся / ударил, переводит дух
            Gloat     // поймал — грозит метлой
        }

        [Header("Погоня")]
        [SerializeField] private float baseSpeed = 4.2f;
        [Tooltip("Рывок, когда цель близко.")]
        [SerializeField] private float lungeMultiplier = 1.6f;
        [SerializeField] private float lungeDistance = 3.5f;
        [SerializeField] private float lungeDuration = 0.5f;
        [SerializeField] private float lungeCooldown = 3f;
        [SerializeField] private float retargetInterval = 0.5f;

        [Header("Удар метлой")]
        [SerializeField] private float swingTriggerDistance = 1.7f;
        [SerializeField] private float windupTime = 0.32f;
        [SerializeField] private float sweepRadius = 2.0f;
        [SerializeField, Range(0f, 180f)] private float sweepHalfAngle = 70f;
        [SerializeField] private float sweepMinY = -0.6f;
        [SerializeField] private float sweepMaxY = 2.0f;
        [SerializeField] private float recoverTime = 0.5f;
        [SerializeField] private float gloatTime = 1.2f;

        [Header("Визуал")]
        [SerializeField] private Transform broomPivot;
        [SerializeField] private Transform bodyRoot;
        [Tooltip("Модель-персонаж с анимациями idle/run (параметр Speed). Если пусто — дворник из примитивов.")]
        [SerializeField] private Animator animator;

        public readonly NetworkVariable<JanitorState> State = new(JanitorState.Idle);

        /// <summary>Сервер: кого сейчас преследует (ulong.MaxValue — никого).</summary>
        public ulong TargetId { get; private set; } = ulong.MaxValue;
        public bool IsActive => State.Value != JanitorState.Idle;

        /// <summary>Сколько секунд длится текущее состояние (у всех игроков, для анимации).</summary>
        public float StateTime => _stateTime;

        /// <summary>Сервер: множитель скорости (растёт со временем, задаёт режим игры).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Сервер: дворник ударил — список пойманных (может быть пустым).</summary>
        public event Action<Janitor, List<CatController>> Swept;

        private NavMeshAgent _agent;
        private double _stateUntil;
        private double _nextRetarget;
        private double _lungeUntil;
        private double _nextLunge;
        private Func<ulong, bool> _isAlive;
        private Func<Janitor, ulong, bool> _isTakenByOther;

        // Визуал (у всех).
        private float _stateTime;
        private Vector3 _lastPosition;
        private float _visualSpeed;
        private float _stepPhase;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        public override void OnNetworkSpawn()
        {
            State.OnValueChanged += (_, _) => _stateTime = 0f;
            _lastPosition = transform.position;

            // Двигает дворника только сервер; у клиентов агент выключен — позиция приходит по сети.
            _agent.enabled = IsServer;
            if (IsServer)
            {
                _agent.speed = baseSpeed;
                _agent.isStopped = true;
            }
        }

        // ───────────────────────── Сервер ─────────────────────────

        /// <summary>Сервер: выпустить дворника на охоту.</summary>
        public void Activate(Func<ulong, bool> isAlive, Func<Janitor, ulong, bool> isTakenByOther)
        {
            if (!IsServer) return;
            _isAlive = isAlive;
            _isTakenByOther = isTakenByOther;
            _nextRetarget = 0;
            SetState(JanitorState.Chase, 0);
        }

        /// <summary>Сервер: остановиться (игра кончилась).</summary>
        public void Stop()
        {
            if (!IsServer) return;
            TargetId = ulong.MaxValue;
            if (_agent.isOnNavMesh) _agent.isStopped = true;
            if (State.Value != JanitorState.Idle) SetState(JanitorState.Gloat, double.MaxValue);
        }

        private void SetState(JanitorState state, double duration)
        {
            State.Value = state;
            _stateUntil = Now + duration;
        }

        private double Now => NetworkManager.ServerTime.Time;

        private void Update()
        {
            if (IsSpawned && IsServer) ServerTick();
            AnimateVisual();
        }

        private void ServerTick()
        {
            if (!_agent.isOnNavMesh) return;

            switch (State.Value)
            {
                case JanitorState.Idle:
                    _agent.isStopped = true;
                    break;

                case JanitorState.Chase:
                    TickChase();
                    break;

                case JanitorState.Windup:
                    _agent.isStopped = true;
                    FaceTarget(12f);
                    if (Now >= _stateUntil) Sweep();
                    break;

                case JanitorState.Recover:
                case JanitorState.Gloat:
                    _agent.isStopped = true;
                    if (Now >= _stateUntil) SetState(JanitorState.Chase, 0);
                    break;
            }
        }

        private void TickChase()
        {
            if (Now >= _nextRetarget)
            {
                _nextRetarget = Now + retargetInterval;
                PickTarget();
            }

            var target = TargetId != ulong.MaxValue ? CatController.FindByOwner(TargetId) : null;
            if (target == null)
            {
                _agent.isStopped = true;
                return;
            }

            Vector3 toTarget = target.transform.position - transform.position;
            float flatDistance = new Vector2(toTarget.x, toTarget.z).magnitude;

            // Рывок, когда цель близко.
            if (flatDistance < lungeDistance && Now >= _nextLunge)
            {
                _lungeUntil = Now + lungeDuration;
                _nextLunge = Now + lungeCooldown;
            }

            float speed = baseSpeed * SpeedMultiplier * (Now < _lungeUntil ? lungeMultiplier : 1f);
            _agent.speed = speed;
            _agent.acceleration = speed * 6f;
            _agent.isStopped = false;
            _agent.SetDestination(target.transform.position);

            if (flatDistance < swingTriggerDistance)
                SetState(JanitorState.Windup, windupTime);
        }

        /// <summary>Ближайший живой кот; чужие цели и смена цели «стоят» дороже — дворники расходятся по котам.</summary>
        private void PickTarget()
        {
            float best = float.MaxValue;
            ulong bestId = ulong.MaxValue;

            foreach (var cat in CatController.All)
            {
                if (cat == null || !cat.IsSpawned) continue;
                ulong id = cat.OwnerClientId;
                if (_isAlive != null && !_isAlive(id)) continue;

                float cost = Vector3.Distance(cat.transform.position, transform.position);
                if (id == TargetId) cost -= 2f;
                if (_isTakenByOther != null && _isTakenByOther(this, id)) cost += 5f;

                if (cost < best)
                {
                    best = cost;
                    bestId = id;
                }
            }

            TargetId = bestId;
        }

        private void FaceTarget(float sharpness)
        {
            var target = TargetId != ulong.MaxValue ? CatController.FindByOwner(TargetId) : null;
            if (target == null) return;
            Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-sharpness * Time.deltaTime));
        }

        private void Sweep()
        {
            var caught = new List<CatController>();
            float cosLimit = Mathf.Cos(sweepHalfAngle * Mathf.Deg2Rad);

            foreach (var cat in CatController.All)
            {
                if (cat == null || !cat.IsSpawned) continue;
                if (_isAlive != null && !_isAlive(cat.OwnerClientId)) continue;

                Vector3 to = cat.transform.position - transform.position;
                if (to.y < sweepMinY || to.y > sweepMaxY) continue;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.magnitude > sweepRadius) continue;
                if (flat.sqrMagnitude > 0.04f && Vector3.Dot(flat.normalized, transform.forward) < cosLimit) continue;

                caught.Add(cat);
            }

            SetState(caught.Count > 0 ? JanitorState.Gloat : JanitorState.Recover, caught.Count > 0 ? gloatTime : recoverTime);
            if (caught.Count > 0) TargetId = ulong.MaxValue;
            Swept?.Invoke(this, caught);
        }

        // ───────────────────────── Визуал (у всех) ─────────────────────────

        private void AnimateVisual()
        {
            _stateTime += Time.deltaTime;

            // Скорость оцениваем по смещению — так работает и у клиентов, где агента нет.
            Vector3 position = transform.position;
            float frameSpeed = Time.deltaTime > 0f ? new Vector3(position.x - _lastPosition.x, 0f, position.z - _lastPosition.z).magnitude / Time.deltaTime : 0f;
            _lastPosition = position;
            _visualSpeed = Mathf.Lerp(_visualSpeed, Mathf.Min(frameSpeed, 10f), 1f - Mathf.Exp(-10f * Time.deltaTime));

            if (animator != null)
            {
                animator.SetFloat(SpeedId, _visualSpeed);
                // Замах — чуть замедляем, радость — пританцовывает быстрее.
                animator.speed = State.Value == JanitorState.Windup ? 0.4f : State.Value == JanitorState.Gloat ? 1.6f : 1f;
            }

            if (bodyRoot != null)
            {
                _stepPhase += _visualSpeed * Time.deltaTime * 2.6f;
                float bob = animator != null ? 0f : Mathf.Abs(Mathf.Sin(_stepPhase)) * Mathf.Clamp01(_visualSpeed / 3f) * 0.12f;
                float lean = Mathf.Clamp01(_visualSpeed / 6f) * 10f;
                if (State.Value == JanitorState.Gloat) bob = Mathf.Abs(Mathf.Sin(_stateTime * 9f)) * 0.08f; // подпрыгивает от радости
                bodyRoot.localPosition = new Vector3(0f, bob, 0f);
                bodyRoot.localRotation = Quaternion.Euler(lean, 0f, 0f);
            }

            if (broomPivot == null) return;

            // Метла: в погоне — наперевес, в замахе — занесена назад и вверх, удар — резко вперёд к земле.
            Vector3 carry = new Vector3(38f, -22f, -10f);
            Vector3 raised = new Vector3(-125f, -25f, -15f);
            Vector3 swept = new Vector3(75f, 50f, -10f);
            Vector3 shake = new Vector3(-95f + Mathf.Sin(_stateTime * 18f) * 25f, 0f, -10f);

            Vector3 euler;
            switch (State.Value)
            {
                case JanitorState.Windup:
                    euler = Vector3.Lerp(carry, raised, Smooth(_stateTime / windupTime));
                    break;
                case JanitorState.Recover:
                    euler = _stateTime < 0.1f
                        ? Vector3.Lerp(raised, swept, _stateTime / 0.1f)
                        : Vector3.Lerp(swept, carry, Smooth((_stateTime - 0.1f) / 0.4f));
                    break;
                case JanitorState.Gloat:
                    euler = _stateTime < 0.1f ? Vector3.Lerp(raised, swept, _stateTime / 0.1f)
                        : _stateTime < 0.35f ? swept
                        : shake;
                    break;
                default:
                    euler = carry + new Vector3(Mathf.Sin(_stepPhase * 2f) * 6f * Mathf.Clamp01(_visualSpeed / 3f), 0f, 0f);
                    break;
            }

            broomPivot.localRotation = Quaternion.Euler(euler);
        }

        private static readonly int SpeedId = Animator.StringToHash("Speed");

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
