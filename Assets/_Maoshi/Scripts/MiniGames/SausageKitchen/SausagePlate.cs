using Maoshi.Cat;
using Maoshi.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.SausageKitchen
{
    /// <summary>
    /// Тарелка с колбасой на столе. Толчок лапой (F) — схватить кусок (решает SausageMode на сервере).
    /// Целая «Докторская» видна на тарелке, пока её никто не унёс.
    /// </summary>
    public class SausagePlate : NetworkBehaviour, IPushReceiver
    {
        [SerializeField] private GameObject stickVisual;

        public SausageMode Mode { get; set; }

        public void OnPushedByCat(CatController pusher, Vector3 direction)
        {
            if (Mode != null) Mode.TryGrab(pusher.OwnerClientId);
        }

        private void Update()
        {
            if (stickVisual == null || Mode == null || !Mode.IsSpawned) return;
            bool show = Mode.StickOnPlate.Value;
            if (stickVisual.activeSelf != show) stickVisual.SetActive(show);
            if (show) stickVisual.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2f) * 6f, 0f);
        }
    }
}
