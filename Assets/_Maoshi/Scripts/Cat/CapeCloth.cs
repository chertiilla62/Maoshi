using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Плащ супергероя — мягкая ткань, которая не перекручивается. Два слоя:
    /// 1) «Каркас» (гид): цепочка рядов от плеч к хвосту, у каждого ряда углы подъёма и отклонения вбок
    ///    на пружинках; поперёк — дуга вокруг спины. Цели углов зависят от движения кота:
    ///    стоит — лежит на спине; бежит — поднимается и полощется волной; падает — подлетает;
    ///    тормозит — встаёт парусом (не на голову); поворачивает — уходит наружу. На каркасе — складки
    ///    и бегущие по ткани волны (по длине и поперёк), поэтому плащ не плоский.
    /// 2) Ткань: каждая точка (сетка 13×9) — verlet с гравитацией и сопротивлением, держит длину соседних
    ///    связей и мягко притягивается к своей точке каркаса (у плеч сильно, к краю и к бокам — слабо).
    ///    Это даёт провисание, запаздывание и хлопанье краёв, а притяжение к каркасу не даёт ткани
    ///    перевернуться или скрутиться.
    /// Точки ткани переносятся вместе с телом (перемещение и поворот корпуса) — на скорости ткань не
    /// отстаёт на метры, «ветер» задаёт каркас. Тело — капсула по костям Spine → Neck.
    /// Скорость считается по перемещению плеч — работает у чужих котов и в витрине меню, без сети.
    /// Меш строится в коде при включении. В редакторе: BuildRestPose() и Tick(dt) вручную (снимки Stage 36).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CapeCloth : MonoBehaviour
    {
        [Header("Форма")]
        [SerializeField] private float length = 0.36f;
        [SerializeField] private float topWidth = 0.15f;
        [SerializeField] private float bottomWidth = 0.28f;
        [Tooltip("Насколько края огибают бока в покое (радианы дуги у нижнего края)")]
        [SerializeField] private float restDrape = 1.45f;
        [Tooltip("Огибание, когда плащ поднят ветром")]
        [SerializeField] private float liftDrape = 0.55f;
        [Tooltip("Глубина складок в покое (м)")]
        [SerializeField] private float foldDepth = 0.015f;

        [Header("Движение каркаса")]
        [SerializeField] private float runLiftDegrees = 36f;
        [SerializeField] private float flutterDegrees = 9f;
        [SerializeField] private float rippleHeight = 0.022f;
        [SerializeField] private float maxSwingDegrees = 92f;
        [SerializeField] private float stiffness = 110f;
        [SerializeField] private float damping = 11f;

        [Header("Ткань")]
        [SerializeField] private float gravity = 4f;
        [SerializeField] private float airDrag = 3f;
        [Tooltip("Притяжение к каркасу у плеч и у нижнего края (1/с)")]
        [SerializeField] private Vector2 pull = new(60f, 9f);
        [Tooltip("Доля движения тела, которую ткань «не успевает» повторить (запаздывание)")]
        [Range(0f, 0.5f)] [SerializeField] private float inertia = 0.12f;
        [SerializeField] private float bodyRadius = 0.088f;

        [Header("Эмблема (едет по ткани)")]
        [SerializeField] private Transform emblem;
        [SerializeField] private float emblemRow = 0.36f;

        private const int Rows = 13;
        private const int Cols = 9;
        private const int Iterations = 3;

        // Каркас: углы рядов (радианы) и скорости.
        private readonly float[] _pitch = new float[Rows];
        private readonly float[] _pitchVel = new float[Rows];
        private readonly float[] _yaw = new float[Rows];
        private readonly float[] _yawVel = new float[Rows];

        private Vector3[] _guide, _p, _prev;
        private float[] _restAcross;
        private float _restAlong;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Transform _spineA, _spineB, _spineRight;

        private Vector3 _lastAnchor, _velocity, _prevVelocity, _accel, _lastTail;
        private Quaternion _lastBody = Quaternion.identity;
        private float _yawRate, _time, _swing, _run;
        private bool _ready;

        private void OnEnable() => BuildRestPose();

        private void OnDisable() => _ready = false;

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        private void LateUpdate()
        {
            if (_ready) Tick(Time.deltaTime);
        }

        /// <summary>Собрать меш и положить плащ в позу покоя (в редакторе — без Play).</summary>
        public void BuildRestPose()
        {
            FindBones();
            BuildMesh();
            Axes(out var right, out var up, out var toTail);
            for (int r = 0; r < Rows; r++)
            {
                _pitch[r] = RestPitch(r);
                _pitchVel[r] = _yaw[r] = _yawVel[r] = 0f;
            }
            _lastAnchor = transform.position;
            _velocity = _prevVelocity = _accel = Vector3.zero;
            _lastTail = Flat(toTail);
            _lastBody = Body(up, toTail);
            _yawRate = _swing = _run = 0f;

            Guide();
            System.Array.Copy(_guide, _p, _p.Length);
            System.Array.Copy(_guide, _prev, _p.Length);
            // Длины связей — по каркасу покоя.
            _restAlong = length / (Rows - 1);
            for (int r = 0; r < Rows; r++)
                _restAcross[r] = Vector3.Distance(_guide[I(r, 0)], _guide[I(r, 1)]);

            _ready = true;
            UpdateMesh();
        }

        /// <summary>Один шаг (в игре — каждый кадр после анимации).</summary>
        public void Tick(float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, 1f / 30f);
            if (dt <= 0f || _p == null) return;
            _time += dt;

            Vector3 anchor = transform.position;
            // Телепорт (респавн, смена сцены) — плащ сразу в покой.
            if ((anchor - _lastAnchor).sqrMagnitude > 1f) { BuildRestPose(); return; }

            Axes(out _, out var up, out var toTail);
            Vector3 forward = -Flat(toTail);

            // Перенести ткань вместе с телом (почти полностью — немного запаздывает).
            Quaternion body = Body(up, toTail);
            Quaternion turnDelta = Quaternion.Slerp(Quaternion.identity, body * Quaternion.Inverse(_lastBody), 1f - inertia);
            Vector3 moveDelta = (anchor - _lastAnchor) * (1f - inertia);
            for (int i = 0; i < _p.Length; i++)
            {
                _p[i] = _lastAnchor + moveDelta + turnDelta * (_p[i] - _lastAnchor);
                _prev[i] = _lastAnchor + moveDelta + turnDelta * (_prev[i] - _lastAnchor);
            }
            _lastBody = body;

            // Сглаженные скорость и ускорение плеч.
            Vector3 rawVelocity = (anchor - _lastAnchor) / dt;
            _lastAnchor = anchor;
            _velocity = Vector3.Lerp(_velocity, rawVelocity, 1f - Mathf.Exp(-dt * 12f));
            Vector3 rawAccel = (_velocity - _prevVelocity) / dt;
            _prevVelocity = _velocity;
            _accel = Vector3.Lerp(_accel, rawAccel, 1f - Mathf.Exp(-dt * 8f));

            Vector3 tail = Flat(toTail);
            float turn = Vector3.SignedAngle(_lastTail, tail, Vector3.up) * Mathf.Deg2Rad / dt;
            _lastTail = tail;
            _yawRate = Mathf.Lerp(_yawRate, turn, 1f - Mathf.Exp(-dt * 10f));

            UpdateGuideAngles(dt, forward);
            Guide();
            SimulateCloth(dt);
            UpdateMesh();
        }

        // ───────────────────────── Каркас ─────────────────────────

        private void UpdateGuideAngles(float dt, Vector3 forward)
        {
            Vector3 flatVelocity = new(_velocity.x, 0f, _velocity.z);
            _run = Mathf.Clamp01(flatVelocity.magnitude / 6f);
            float fall = Mathf.Clamp(-_velocity.y * 0.12f, -0.25f, 0.7f);
            float brake = Mathf.Clamp(-Vector3.Dot(_accel, forward) * 0.045f, 0f, 1.2f);
            float maxSwing = maxSwingDegrees * Mathf.Deg2Rad;
            _swing = Mathf.Clamp(_run * _run * runLiftDegrees * Mathf.Deg2Rad + fall + brake, -0.2f, maxSwing);
            float sway = Mathf.Clamp(-_yawRate * 0.12f, -0.6f, 0.6f);
            float flutter = flutterDegrees * Mathf.Deg2Rad * Mathf.Clamp01(_run * 2.5f + Mathf.Abs(fall));

            for (int r = 1; r < Rows; r++)
            {
                float v = r / (float)(Rows - 1);
                float k = stiffness * (1f - 0.55f * v);
                float targetPitch = RestPitch(r) + _swing * (0.75f + 0.25f * v)
                                    + Mathf.Sin(_time * 13f - r * 0.75f) * flutter * v;
                float targetYaw = sway * v + Mathf.Sin(_time * 7f - r * 0.5f) * flutter * 0.4f * v;

                _pitchVel[r] += (k * (targetPitch - _pitch[r]) - damping * _pitchVel[r]) * dt;
                _pitch[r] = Mathf.Clamp(_pitch[r] + _pitchVel[r] * dt, -0.6f, maxSwing + 0.15f);
                _yawVel[r] += (k * (targetYaw - _yaw[r]) - damping * _yawVel[r]) * dt;
                _yaw[r] = Mathf.Clamp(_yaw[r] + _yawVel[r] * dt, -0.8f, 0.8f);
            }
        }

        /// <summary>В покое ряды чуть загибаются вниз по спине к хвосту.</summary>
        private static float RestPitch(int r) => -0.024f * r;

        /// <summary>Точки каркаса: центральная линия цепочкой, поперёк — дуга вокруг спины, складки и волны.</summary>
        private void Guide()
        {
            Axes(out var right, out var up, out var toTail);
            float segment = length / (Rows - 1);
            float lifted = Mathf.Clamp01(_swing / 0.6f);
            // В покое ткань чуть «дышит», на бегу — полощется.
            float ripple = 0.003f + rippleHeight * Mathf.Clamp01(_run * 1.5f + lifted * 0.5f);

            Vector3 center = transform.position;
            for (int r = 0; r < Rows; r++)
            {
                Quaternion yaw = Quaternion.AngleAxis(_yaw[r] * Mathf.Rad2Deg, up);
                Vector3 along = yaw * (toTail * Mathf.Cos(_pitch[r]) + up * Mathf.Sin(_pitch[r]));
                Vector3 across = yaw * right;
                Vector3 normal = Vector3.Cross(across, along).normalized;
                if (r > 0) center = PushOut(center + along * segment, 0.012f);

                float v = r / (float)(Rows - 1);
                float half = Mathf.Lerp(topWidth, bottomWidth, v) * 0.5f;
                float drape = Mathf.Lerp(restDrape, liftDrape, lifted) * Mathf.Lerp(0.7f, 1f, v);
                float radius = half / drape;
                for (int c = 0; c < Cols; c++)
                {
                    float u = c / (float)(Cols - 1) * 2f - 1f;
                    float a = u * drape;
                    // Складки: мягкие продольные волны поперёк ткани, глубже к низу. На бегу — бегущая рябь.
                    float fold = foldDepth * v * Mathf.Cos(u * Mathf.PI * 2.5f)
                                 + ripple * v * Mathf.Sin(_time * 17f - r * 0.95f + u * 2.4f);
                    // Низ плаща чуть длиннее по центру — не «обрезанный» край.
                    float tip = r == Rows - 1 ? segment * 0.35f * (1f - u * u) : 0f;
                    Vector3 p = center + along * tip
                                + across * (Mathf.Sin(a) * radius)
                                - normal * ((1f - Mathf.Cos(a)) * radius)
                                + normal * fold;
                    _guide[I(r, c)] = p;
                }
            }
        }

        // ───────────────────────── Ткань ─────────────────────────

        private void SimulateCloth(float dt)
        {
            float keep = Mathf.Clamp01(1f - airDrag * dt);
            Vector3 g = Vector3.down * (gravity * dt * dt);

            for (int r = 0; r < Rows; r++)
            {
                float v = r / (float)(Rows - 1);
                for (int c = 0; c < Cols; c++)
                {
                    int i = I(r, c);
                    if (r == 0)
                    {
                        _p[i] = _prev[i] = _guide[i]; // пришит к плечам
                        continue;
                    }
                    Vector3 velocity = (_p[i] - _prev[i]) * keep;
                    _prev[i] = _p[i];
                    _p[i] += velocity + g;

                    // Притяжение к каркасу: у плеч сильное, к низу и к боковым краям — слабое (края хлопают).
                    float u = Mathf.Abs(c / (float)(Cols - 1) * 2f - 1f);
                    float k = Mathf.Lerp(pull.x, pull.y, Mathf.Sqrt(v)) * (1f - 0.35f * u);
                    _p[i] = Vector3.Lerp(_p[i], _guide[i], 1f - Mathf.Exp(-k * dt));
                }
            }

            for (int it = 0; it < Iterations; it++)
            {
                for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    if (c + 1 < Cols) Link(I(r, c), I(r, c + 1), _restAcross[r]);
                    if (r + 1 < Rows) Link(I(r, c), I(r + 1, c), _restAlong);
                }
                for (int i = Cols; i < _p.Length; i++) _p[i] = PushOut(_p[i], 0.008f);
            }
        }

        /// <summary>Ткань не растягивается (сжиматься может — так получаются складки).</summary>
        private void Link(int a, int b, float rest)
        {
            bool pinA = a < Cols, pinB = b < Cols;
            if (pinA && pinB) return;
            Vector3 delta = _p[b] - _p[a];
            float d = delta.magnitude;
            if (d <= rest || d < 1e-6f) return;
            Vector3 fix = delta * ((d - rest) / d);
            if (pinA) _p[b] -= fix;
            else if (pinB) _p[a] += fix;
            else { _p[a] += fix * 0.5f; _p[b] -= fix * 0.5f; }
        }

        // ───────────────────────── Кости, оси, тело ─────────────────────────

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.back;
        }

        private static Quaternion Body(Vector3 up, Vector3 toTail) => Quaternion.LookRotation(-toTail, up);

        private void FindBones()
        {
            Transform root = transform;
            while (root.parent != null && root.name != "Model") root = root.parent;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Spine") _spineA = t;
                else if (t.name == "Neck") _spineB = t;
                else if (t.name == "Spine.3") _spineRight = t;
            }
        }

        /// <summary>Оси тела: вправо, вверх, к хвосту.</summary>
        private void Axes(out Vector3 right, out Vector3 up, out Vector3 toTail)
        {
            if (_spineA != null && _spineB != null && _spineRight != null)
            {
                toTail = (_spineA.position - _spineB.position).normalized;
                right = _spineRight.right;
                right = (right - Vector3.Dot(right, toTail) * toTail).normalized;
                up = Vector3.Cross(right, toTail).normalized;
            }
            else
            {
                right = transform.right;
                toTail = -transform.forward;
                up = transform.up;
            }
        }

        private Vector3 BodyCenter(Vector3 p)
        {
            if (_spineA == null || _spineB == null) return transform.position - Vector3.up * bodyRadius;
            Vector3 a = _spineA.position, b = _spineB.position;
            Vector3 ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return a + ab * k;
        }

        /// <summary>Вытолкнуть точку из капсулы тела.</summary>
        private Vector3 PushOut(Vector3 p, float gap)
        {
            if (_spineA == null || _spineB == null) return p;
            Vector3 center = BodyCenter(p);
            Vector3 off = p - center;
            float d = off.magnitude;
            float min = bodyRadius + gap;
            return d < min && d > 1e-5f ? center + off * (min / d) : p;
        }

        // ───────────────────────── Меш ─────────────────────────

        private int I(int r, int c) => r * Cols + c;

        private void BuildMesh()
        {
            int n = Rows * Cols;
            _guide ??= new Vector3[n];
            _p ??= new Vector3[n];
            _prev ??= new Vector3[n];
            _restAcross ??= new float[Rows];
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "CapeCloth", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }
            _vertices = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                var t = new Vector2(c / (float)(Cols - 1), 1f - r / (float)(Rows - 1));
                uv[I(r, c)] = t;
                uv[n + I(r, c)] = t;
            }

            // Две стороны — две подсетки: снаружи (красная) и подкладка (золотая).
            var outer = new int[(Rows - 1) * (Cols - 1) * 6];
            var inner = new int[outer.Length];
            int k = 0;
            for (int r = 0; r < Rows - 1; r++)
            for (int c = 0; c < Cols - 1; c++)
            {
                int a = I(r, c), b = I(r, c + 1), d = I(r + 1, c), e = I(r + 1, c + 1);
                outer[k] = a; outer[k + 1] = b; outer[k + 2] = d;
                outer[k + 3] = b; outer[k + 4] = e; outer[k + 5] = d;
                inner[k] = n + a; inner[k + 1] = n + d; inner[k + 2] = n + b;
                inner[k + 3] = n + b; inner[k + 4] = n + d; inner[k + 5] = n + e;
                k += 6;
            }
            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(outer, 0);
            _mesh.SetTriangles(inner, 1);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void UpdateMesh()
        {
            if (_mesh == null) return;
            int n = Rows * Cols;
            for (int i = 0; i < n; i++)
            {
                Vector3 local = transform.InverseTransformPoint(_p[i]);
                _vertices[i] = local;
                _vertices[n + i] = local;
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            PlaceEmblem();
        }

        private void PlaceEmblem()
        {
            if (emblem == null) return;
            int r = Mathf.Clamp(Mathf.RoundToInt(emblemRow * (Rows - 1)), 1, Rows - 2);
            int c = Cols / 2;
            Vector3 p = _p[I(r, c)];
            Vector3 along = _p[I(r + 1, c)] - _p[I(r - 1, c)];
            Vector3 across = _p[I(r, c + 1)] - _p[I(r, c - 1)];
            Vector3 normal = Vector3.Cross(across, along);
            if (normal.sqrMagnitude < 1e-8f || along.sqrMagnitude < 1e-8f) return;
            normal.Normalize();
            if (Vector3.Dot(normal, p - BodyCenter(p)) < 0f) normal = -normal;
            emblem.position = p + normal * 0.004f;
            emblem.rotation = Quaternion.LookRotation(-normal, -along.normalized);
        }
    }
}
