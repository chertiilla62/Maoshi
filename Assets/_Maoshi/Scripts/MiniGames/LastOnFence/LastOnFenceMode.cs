using Maoshi.Cat;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.LastOnFence
{
    /// <summary>
    /// «Последний на заборе»: все коты на узком заборе, толкаются. Кто упал — выбыл.
    /// Чтобы игра не тянулась, забор разрушается с краёв: каждые N секунд пропадает по секции с каждой стороны.
    /// Выбывших переносим на «скамейку» — оттуда можно смотреть и болеть.
    /// </summary>
    public class LastOnFenceMode : MiniGameMode
    {
        [Header("Забор")]
        [Tooltip("Секции забора по порядку от одного края к другому.")]
        [SerializeField] private Transform[] segments;
        [SerializeField] private float fenceTopY = 2.5f;
        [Tooltip("Насколько ниже верха забора кот считается упавшим.")]
        [SerializeField] private float eliminationDepth = 1.2f;
        [SerializeField] private float collapseInterval = 10f;
        [SerializeField] private int minSegmentsLeft = 4;

        [Header("Скамейка выбывших")]
        [SerializeField] private Transform bench;
        [SerializeField] private float benchSpacing = 1.5f;

        /// <summary>Сколько пар крайних секций уже рухнуло. Меняет сервер, применяют все.</summary>
        public readonly NetworkVariable<int> CollapsedPairs = new();

        private int _benchIndex;

        public override string StatusText =>
            CollapsedPairs.Value > 0 ? "Забор рушится с краёв!" : "Сталкивай остальных с забора!";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CollapsedPairs.OnValueChanged += OnCollapsedChanged;
            ApplyCollapse(CollapsedPairs.Value);
        }

        public override void OnNetworkDespawn()
        {
            CollapsedPairs.OnValueChanged -= OnCollapsedChanged;
            base.OnNetworkDespawn();
        }

        protected override void OnGameTick()
        {
            // 1. Кто ниже забора — выбыл. Идём с конца: Eliminate удаляет из списка.
            for (int i = AlivePlayers.Count - 1; i >= 0; i--)
            {
                ulong clientId = AlivePlayers[i];
                var cat = CatController.FindByOwner(clientId);
                if (cat == null) continue;

                if (cat.transform.position.y < fenceTopY - eliminationDepth)
                {
                    if (RescueDuringStartGrace(cat)) continue;
                    SendToBench(cat);
                    Eliminate(clientId);
                }
            }

            // 2. Забор рушится с краёв по таймеру, но несколько центральных секций остаются.
            if (segments == null || segments.Length == 0) return;
            int maxPairs = Mathf.Max(0, (segments.Length - minSegmentsLeft) / 2);
            int due = Mathf.Min(maxPairs, (int)(ElapsedPlaying / collapseInterval));
            if (due > CollapsedPairs.Value) CollapsedPairs.Value = due;
        }

        private void SendToBench(CatController cat)
        {
            if (bench == null) return;

            // Рядами по 4 кота.
            int column = _benchIndex % 4;
            int row = _benchIndex / 4;
            _benchIndex++;

            Vector3 position = bench.position
                               + bench.right * ((column - 1.5f) * benchSpacing)
                               + bench.forward * (row * benchSpacing);
            cat.SetRespawnAndTeleportRpc(position, bench.rotation);
        }

        private void OnCollapsedChanged(int previous, int next) => ApplyCollapse(next);

        private void ApplyCollapse(int pairs)
        {
            if (segments == null) return;

            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                bool collapsed = i < pairs || i >= segments.Length - pairs;
                segments[i].gameObject.SetActive(!collapsed);
            }
        }
    }
}
