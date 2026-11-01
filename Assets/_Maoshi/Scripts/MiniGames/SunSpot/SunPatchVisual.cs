using UnityEngine;

namespace Maoshi.MiniGames.SunSpot
{
    /// <summary>
    /// Солнечный свет из окна (только у клиентов, на игру не влияет):
    /// - Настоящий источник: прожектор далеко за окном (лучи почти параллельные, как у солнца) с маской-«печеньем»
    ///   (cookie) в форме окна — на полу, мебели и котах ложится светлый прямоугольник окна с мягкими краями,
    ///   мебель отбрасывает тени. Мультяшный шейдер учитывает доп. источники только в этой сцене
    ///   (глобальное _MaoshiExtraLights = 1, пока компонент включён). Яркость в пятне постоянна (∝ расстоянию²).
    /// - «Лучи в воздухе»: прозрачная призма по форме окна от проёма до пятна, по граням — полосы света
    ///   (текстура лучей медленно плывёт), сильнее у окна, гаснет к полу. Плюс пылинки, кружащиеся в луче.
    /// Облако — свет гаснет; последние лучи — свет оранжевее и пульсирует.
    /// </summary>
    public class SunPatchVisual : MonoBehaviour
    {
        [SerializeField] private MeshFilter shaft;
        [Tooltip("Рисовать полупрозрачные «лучи в воздухе» (по умолчанию выключено: свет видно по пятну и пылинкам).")]
        [SerializeField] private bool drawShaft;
        [SerializeField] private Color shaftColor = new(1f, 0.86f, 0.55f, 0.16f);
        [SerializeField] private Color boostShaftColor = new(1f, 0.66f, 0.3f, 0.22f);

        [Header("Свет")]
        [SerializeField] private Light sunLight;
        [Tooltip("Яркость в пятне (интенсивность прожектора = это × расстояние²).")]
        [SerializeField] private float brightness = 1.6f;
        [SerializeField] private Color lightColor = new(1f, 0.86f, 0.6f);
        [SerializeField] private Color boostLightColor = new(1f, 0.62f, 0.32f);
        [Tooltip("Доля маски-окна по ширине (как в текстуре cookie): угол прожектора подбирается под неё.")]
        [SerializeField] private float cookieWidth = 0.9f;

        [Header("Пылинки в луче")]
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Mesh dustMesh;
        [SerializeField] private int dustCount = 70;

        private Mesh _shaft;
        private float _alpha;
        private Vector3 _window, _direction, _spot;
        private bool _hasShown;
        private Transform[] _dust;
        private MeshRenderer[] _dustRenderers;
        private Vector3[] _dustSeed;
        private MaterialPropertyBlock _dustBlock, _shaftBlock;
        private static readonly int ExtraLightsId = Shader.PropertyToID("_MaoshiExtraLights");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        private void Awake()
        {
            _shaft = new Mesh { name = "SunShaft" };
            _shaft.MarkDynamic();
            if (shaft != null) shaft.sharedMesh = _shaft;
            _shaftBlock = new MaterialPropertyBlock();
            _dustBlock = new MaterialPropertyBlock();
            if (dustMaterial == null || dustMesh == null) return;
            _dust = new Transform[dustCount];
            _dustRenderers = new MeshRenderer[dustCount];
            _dustSeed = new Vector3[dustCount];
            for (int i = 0; i < dustCount; i++)
            {
                var go = new GameObject("Dust");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = dustMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = dustMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _dust[i] = go.transform;
                _dustRenderers[i] = r;
                _dustSeed[i] = new Vector3(Random.value, Random.value, Random.value);
            }
        }

        private void OnEnable() => Shader.SetGlobalFloat(ExtraLightsId, 1f);

        private void OnDisable() => Shader.SetGlobalFloat(ExtraLightsId, 0f);

        private void OnDestroy()
        {
            if (_shaft != null) Destroy(_shaft);
        }

        private void LateUpdate()
        {
            var mode = SunSpotMode.Instance;
            if (mode == null || !mode.IsSpawned)
            {
                if (sunLight != null) sunLight.enabled = false;
                return;
            }

            // В облако свет гаснет на месте; выглянуло — плавно разгорается уже в новом месте.
            bool hidden = mode.Cloudy;
            _alpha = Mathf.MoveTowards(_alpha, hidden ? 0f : 1f, Time.deltaTime * (hidden ? 4f : 2f));
            if (!hidden || !_hasShown)
            {
                mode.Beam(mode.NowTime, out _window, out _direction, out _spot);
                _hasShown = true;
            }

            bool boost = mode.FinalBoost;
            float pulse = boost ? 0.88f + 0.12f * Mathf.Sin(Time.time * 6f) : 1f;
            Vector3 apex = mode.Apex(_window, _direction);
            SunSpotMode.BeamAxes(_direction, out var right, out var up);
            mode.BeamTangents(apex, _spot, out float tanW, out float tanH);

            UpdateLight(apex, tanW, boost, pulse);
            Color s = boost ? boostShaftColor : shaftColor;
            s.a *= _alpha * pulse;
            if (drawShaft) BuildShaft(apex, right, up, tanW, tanH, s);
            else if (shaft != null && shaft.gameObject.activeSelf) shaft.gameObject.SetActive(false);
            UpdateDust(apex, right, up, tanW, tanH, s.a);
        }

        private void UpdateLight(Vector3 apex, float tanW, bool boost, float pulse)
        {
            if (sunLight == null) return;
            float distance = Vector3.Distance(apex, _spot);
            sunLight.transform.SetPositionAndRotation(apex, Quaternion.LookRotation(_direction, Vector3.up));
            // Маска-окно занимает cookieWidth от ширины прожектора: угол — чтобы ширина окна совпала с игровой.
            float spot = 2f * Mathf.Atan(tanW / cookieWidth) * Mathf.Rad2Deg;
            sunLight.spotAngle = spot;
            sunLight.innerSpotAngle = spot * 0.98f;
            sunLight.range = distance + 4f;
            sunLight.color = boost ? boostLightColor : lightColor;
            sunLight.intensity = brightness * distance * distance * _alpha * (boost ? 1.15f * pulse : 1f);
            sunLight.enabled = _alpha > 0.01f;
        }

        /// <summary>
        /// Лучи в воздухе: призма-«рамка окна» от проёма до пятна — 4 грани и 2 диагональных листа внутри,
        /// на них текстура полос; ярче у окна, к полу гаснет.
        /// </summary>
        private void BuildShaft(Vector3 apex, Vector3 right, Vector3 up, float tanW, float tanH, Color tint)
        {
            float start = SunSpotMode.ApexBack - 0.2f;                 // у проёма окна
            float end = Vector3.Distance(apex, _spot) - 0.05f;          // у пятна
            Vector3 Corner(float along, float x, float y) =>
                transform.InverseTransformPoint(apex + _direction * along + right * (x * tanW * along) + up * (y * tanH * along));

            // Пары (x, y) краёв для 6 листов: 4 грани и 2 диагонали.
            (float, float, float, float)[] sheets =
            {
                (-1, -1, 1, -1), (1, -1, 1, 1), (1, 1, -1, 1), (-1, 1, -1, -1), (-1, -1, 1, 1), (-1, 1, 1, -1),
            };
            const int rows = 6;
            var vertices = new Vector3[sheets.Length * (rows + 1) * 2];
            var colors = new Color[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[sheets.Length * rows * 6];
            int v = 0, t = 0;
            for (int s = 0; s < sheets.Length; s++)
            {
                var (x0, y0, x1, y1) = sheets[s];
                bool inner = s >= 4;
                for (int r = 0; r <= rows; r++)
                {
                    float k = r / (float)rows;
                    float along = Mathf.Lerp(start, end, k);
                    // Ярче у окна, мягкий вход и гаснет к полу.
                    float fade = Mathf.Clamp01(k * 6f) * (1f - k * 0.85f) * (inner ? 0.7f : 1f);
                    Color c = tint;
                    c.a *= fade;
                    vertices[v] = Corner(along, x0, y0);
                    vertices[v + 1] = Corner(along, x1, y1);
                    colors[v] = c;
                    colors[v + 1] = c;
                    uvs[v] = new Vector2(s * 0.37f, k);
                    uvs[v + 1] = new Vector2(s * 0.37f + 1f, k);
                    if (r < rows)
                    {
                        triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 1;
                        triangles[t++] = v + 1; triangles[t++] = v + 2; triangles[t++] = v + 3;
                    }
                    v += 2;
                }
            }
            _shaft.Clear();
            _shaft.vertices = vertices;
            _shaft.colors = colors;
            _shaft.uv = uvs;
            _shaft.triangles = triangles;
            _shaft.RecalculateBounds();

            // Полосы медленно плывут поперёк луча.
            if (shaft != null)
            {
                var r = shaft.GetComponent<MeshRenderer>();
                r.GetPropertyBlock(_shaftBlock);
                _shaftBlock.SetVector(BaseMapStId, new Vector4(1f, 1f, Time.time * 0.012f, 0f));
                r.SetPropertyBlock(_shaftBlock);
            }
        }

        /// <summary>Пылинки: медленно кружатся и плывут внутри луча, мерцают.</summary>
        private void UpdateDust(Vector3 apex, Vector3 right, Vector3 up, float tanW, float tanH, float shaftAlpha)
        {
            if (_dust == null) return;
            float start = SunSpotMode.ApexBack, end = Vector3.Distance(apex, _spot);
            float time = Time.time;
            for (int i = 0; i < _dust.Length; i++)
            {
                var seed = _dustSeed[i];
                float k = Mathf.Repeat(seed.x + time * 0.01f * (0.5f + seed.z), 1f);
                float along = Mathf.Lerp(start, end, k);
                float x = Mathf.Sin(seed.y * 40f + time * 0.3f * (seed.z - 0.5f)) * 0.9f;
                float y = Mathf.Cos(seed.z * 37f + time * 0.23f * (seed.y - 0.5f)) * 0.9f;
                _dust[i].position = apex + _direction * along + right * (x * tanW * along) + up * (y * tanH * along)
                                    + Vector3.up * (Mathf.Sin(time * 0.6f + seed.y * 9f) * 0.04f);
                _dust[i].localScale = Vector3.one * (0.012f + 0.016f * seed.z);
                float twinkle = 0.5f + 0.5f * Mathf.Sin(time * 2.1f + seed.x * 17f);
                float fade = Mathf.Sin(k * Mathf.PI);
                _dustBlock.SetColor(BaseColorId, new Color(1f, 0.93f, 0.75f, Mathf.Clamp01(shaftAlpha * 6f * twinkle * fade)));
                _dustRenderers[i].SetPropertyBlock(_dustBlock);
            }
        }
    }
}
