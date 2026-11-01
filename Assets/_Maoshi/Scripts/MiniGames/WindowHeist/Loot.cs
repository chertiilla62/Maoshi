using Maoshi.Cat;
using Maoshi.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.WindowHeist
{
    /// <summary>
    /// Добыча в квартире: лежит (Free) → в зубах у кота (Carried) → на балконе в куче (Delivered).
    /// Состояние и место решает сервер (WindowMode); клиенты только показывают.
    /// Толчок лапой (F) по добыче — взять (если зубы свободны).
    /// </summary>
    public class Loot : NetworkBehaviour, IPushReceiver
    {
        public enum LootState : byte { Free, Carried, Delivered }

        [Tooltip("Очки за доставку на балкон.")]
        [SerializeField] private int value = 1;
        [Tooltip("0 — лёгкая, 1 — средняя (чуть медленнее и громче), 2 — тяжёлая (медленно, шумит даже крадучись).")]
        [SerializeField] private int weight;
        [SerializeField] private string title = "Сосиска";
        [Tooltip("Модель (дочерний объект) — её же копия едет в зубах у кота.")]
        [SerializeField] private Transform model;

        public readonly NetworkVariable<LootState> State = new(LootState.Free);
        public readonly NetworkVariable<ulong> Carrier = new(ulong.MaxValue);
        public readonly NetworkVariable<Vector3> Position = new();
        public readonly NetworkVariable<float> Yaw = new();

        public int Value => value;
        public int Weight => weight;
        public string Title => title;
        public Transform Model => model;
        public WindowMode Mode { get; set; }

        private Collider[] _colliders;
        private GameObject _carryVisual;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Position.Value = transform.position;
                Yaw.Value = transform.eulerAngles.y;
            }
            State.OnValueChanged += (_, _) => Refresh();
            Position.OnValueChanged += (_, _) => Refresh();
            Refresh();
        }

        public void OnPushedByCat(CatController pusher, Vector3 direction)
        {
            if (Mode != null) Mode.TryPick(this, pusher.OwnerClientId);
        }

        // ───────────────────────── Сервер ─────────────────────────

        public void ServerCarry(ulong clientId)
        {
            Carrier.Value = clientId;
            State.Value = LootState.Carried;
        }

        public void ServerDrop(Vector3 at)
        {
            Position.Value = at;
            Yaw.Value = Random.Range(0f, 360f);
            Carrier.Value = ulong.MaxValue;
            State.Value = LootState.Free;
        }

        public void ServerDeliver(Vector3 pile)
        {
            Position.Value = pile;
            Carrier.Value = ulong.MaxValue;
            State.Value = LootState.Delivered;
        }

        // ───────────────────────── Картинка ─────────────────────────

        private void Refresh()
        {
            bool free = State.Value == LootState.Free;
            foreach (var c in _colliders) if (c != null) c.enabled = free;
            if (model != null) model.gameObject.SetActive(State.Value != LootState.Carried);
            if (State.Value != LootState.Carried)
            {
                transform.SetPositionAndRotation(Position.Value, Quaternion.Euler(0f, Yaw.Value, 0f));
                if (_carryVisual != null) { Destroy(_carryVisual); _carryVisual = null; }
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned || State.Value != LootState.Carried || model == null) return;
            var cat = CatController.FindByOwner(Carrier.Value);
            if (cat == null) return;
            if (_carryVisual == null)
            {
                _carryVisual = Instantiate(model.gameObject);
                _carryVisual.SetActive(true);
                foreach (var c in _carryVisual.GetComponentsInChildren<Collider>(true)) Destroy(c);
                _carryVisual.transform.localScale = model.lossyScale * (weight >= 2 ? 0.8f : 1f);
            }
            var t = cat.transform;
            // В зубах, поперёк морды; тяжёлое — пониже.
            float up = weight >= 2 ? 0.28f : 0.36f;
            _carryVisual.transform.SetPositionAndRotation(t.position + t.up * up + t.forward * 0.42f, t.rotation * Quaternion.Euler(0f, 90f, 0f));
        }

        public override void OnDestroy()
        {
            if (_carryVisual != null) Destroy(_carryVisual);
            base.OnDestroy();
        }
    }
}
