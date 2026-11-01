using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.WhoBrokeVase
{
    /// <summary>
    /// Бабушкина ваза. Состояние решает сервер (VaseMode), клиенты только показывают:
    /// - Wobble: качнулась от лапы и успокоилась (так выглядит любой толчок — хоть невинный, хоть пакостный).
    /// - Doomed: раскачивается всё сильнее и через пару секунд упадёт, если её не поймать лапой.
    /// - Broken: опрокинулась и разбилась вдребезги.
    /// Толчок лапой (F) приходит через CatController → IPushReceiver только на сервере.
    /// </summary>
    public class Vase : NetworkBehaviour, IPushReceiver
    {
        public enum VaseState : byte { Standing, Wobble, Doomed, Broken }

        [SerializeField] private Transform model;
        [SerializeField] private Color shardColor = new(0.35f, 0.55f, 0.85f);

        public readonly NetworkVariable<VaseState> State = new(VaseState.Standing);
        public readonly NetworkVariable<double> StateTime = new();
        /// <summary>Через сколько секунд после начала раскачки ваза упадёт (для красивой анимации у всех).</summary>
        public readonly NetworkVariable<float> FallDelay = new(2f);

        /// <summary>Сервер: кто обречил вазу (клиент или VaseMode.Draft).</summary>
        public ulong Culprit { get; private set; } = VaseMode.NoOne;
        public VaseMode Mode { get; set; }

        private Quaternion _restRotation;
        private Vector3 _restPosition;
        private Vector3 _tiltAxis = Vector3.right;
        private float _fallT = -1f;
        private bool _shattered;

        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        private void Awake()
        {
            if (model == null) model = transform;
            _restRotation = model.localRotation;
            _restPosition = model.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            State.OnValueChanged += OnStateChanged;
            if (State.Value == VaseState.Broken) SetBrokenInstantly();
        }

        public override void OnNetworkDespawn() => State.OnValueChanged -= OnStateChanged;

        // ───────────────────────── Сервер ─────────────────────────

        public void OnPushedByCat(CatController pusher, Vector3 direction)
        {
            if (Mode != null) Mode.OnVaseNudged(this, pusher.OwnerClientId, direction);
        }

        public void ServerWobble()
        {
            if (State.Value == VaseState.Broken || State.Value == VaseState.Doomed) return;
            StateTime.Value = Now;
            State.Value = VaseState.Wobble;
            // Значение то же — всё равно перезапустить анимацию у всех.
            WobbleAgainRpc();
        }

        public void ServerDoom(ulong culprit, float delay)
        {
            Culprit = culprit;
            FallDelay.Value = delay;
            StateTime.Value = Now;
            State.Value = VaseState.Doomed;
        }

        public void ServerSave()
        {
            Culprit = VaseMode.NoOne;
            StateTime.Value = Now;
            State.Value = VaseState.Wobble;
            SavedRpc();
        }

        public void ServerBreak()
        {
            StateTime.Value = Now;
            State.Value = VaseState.Broken;
        }

        public void ServerSettle()
        {
            if (State.Value == VaseState.Wobble) State.Value = VaseState.Standing;
        }

        public float StateAge => (float)(Now - StateTime.Value);

        [Rpc(SendTo.Everyone)]
        private void WobbleAgainRpc()
        {
            _tiltAxis = RandomAxis();
            AudioManager.PlayAt("clink", transform.position, 0.6f, 0.1f, 20f);
        }

        [Rpc(SendTo.Everyone)]
        private void SavedRpc() => AudioManager.PlayAt("ui_confirm", transform.position + Vector3.up * 0.5f, 0.9f, 0f, 25f);

        // ───────────────────────── Клиенты ─────────────────────────

        private void OnStateChanged(VaseState previous, VaseState next)
        {
            if (next == VaseState.Doomed)
            {
                _tiltAxis = RandomAxis();
                AudioManager.PlayAt("clink", transform.position, 0.7f, 0.1f, 20f);
            }
            else if (next == VaseState.Broken)
            {
                _fallT = 0f;
                foreach (var c in GetComponents<Collider>()) c.enabled = false;
            }
            else if (next == VaseState.Standing && _shattered)
            {
                // Новая игра в той же сцене не предусмотрена, но на всякий случай — вернуть вазу.
                _shattered = false;
                model.gameObject.SetActive(true);
            }
        }

        private static Vector3 RandomAxis()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        private void Update()
        {
            if (!IsSpawned || model == null || _shattered) return;
            float age = StateAge;

            switch (State.Value)
            {
                case VaseState.Standing:
                    model.localRotation = Quaternion.Slerp(model.localRotation, _restRotation, 1f - Mathf.Exp(-12f * Time.deltaTime));
                    break;

                case VaseState.Wobble:
                {
                    float amp = 11f * Mathf.Exp(-age * 3.5f);
                    float angle = amp * Mathf.Sin(age * 19f);
                    model.localRotation = _restRotation * Quaternion.AngleAxis(angle, transform.InverseTransformDirection(_tiltAxis));
                    break;
                }

                case VaseState.Doomed:
                {
                    // Сначала как обычная раскачка, потом всё сильнее — до самого падения.
                    float k = Mathf.Clamp01(age / Mathf.Max(0.1f, FallDelay.Value));
                    float amp = Mathf.Lerp(11f, 24f, k * k);
                    float angle = amp * Mathf.Sin(age * Mathf.Lerp(19f, 13f, k));
                    model.localRotation = _restRotation * Quaternion.AngleAxis(angle, transform.InverseTransformDirection(_tiltAxis));
                    break;
                }

                case VaseState.Broken:
                    AnimateFall();
                    break;
            }
        }

        private void AnimateFall()
        {
            if (_fallT < 0f) _fallT = 0f;
            _fallT += Time.deltaTime;
            float tip = Mathf.Clamp01(_fallT / 0.35f);
            model.localRotation = _restRotation * Quaternion.AngleAxis(95f * tip * tip, transform.InverseTransformDirection(_tiltAxis));
            // Падение с полки/стола до пола.
            float height = transform.position.y;
            float drop = Mathf.Min(height, 4.9f * Mathf.Max(0f, _fallT - 0.2f) * Mathf.Max(0f, _fallT - 0.2f));
            model.localPosition = _restPosition + transform.InverseTransformDirection(_tiltAxis * 0.25f * tip + Vector3.down * drop);
            if (_fallT > 0.25f && drop >= height - 0.01f) Shatter();
            else if (_fallT > 1.5f) Shatter();
        }

        private void SetBrokenInstantly()
        {
            _shattered = true;
            if (model != null) model.gameObject.SetActive(false);
        }

        private void Shatter()
        {
            _shattered = true;
            Vector3 at = model.position;
            model.gameObject.SetActive(false);
            AudioManager.PlayAt("vase_break", at, 1f, 0.08f, 40f);

            // Черепки — только для красоты, у каждого игрока свои. Материал — как у самой вазы.
            var source = model.GetComponentInChildren<Renderer>(true);
            Material material = source != null ? source.sharedMaterial : null;
            bool own = material == null;
            if (own)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", shardColor);
            }
            for (int i = 0; i < 12; i++)
            {
                var shard = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                shard.name = "Shard";
                shard.transform.position = at + Random.insideUnitSphere * 0.15f + Vector3.up * 0.1f;
                shard.transform.localScale = new Vector3(Random.Range(0.06f, 0.16f), Random.Range(0.03f, 0.07f), Random.Range(0.06f, 0.14f));
                shard.transform.rotation = Random.rotation;
                shard.GetComponent<Renderer>().sharedMaterial = material;
                var body = shard.AddComponent<Rigidbody>();
                body.mass = 0.05f;
                body.linearVelocity = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(1.5f, 3.5f), Random.Range(-2.5f, 2.5f));
                body.angularVelocity = Random.insideUnitSphere * 12f;
                Destroy(shard, Random.Range(5f, 7f));
            }
            if (own) Destroy(material, 8f);
        }
    }
}
