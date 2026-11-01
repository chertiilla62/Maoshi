using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.Audio
{
    /// <summary>
    /// Голос кота по сети: E — мяукнуть, Q — зашипеть, R — «мрр?». Слышат все рядом.
    /// Владелец просит сервер, сервер рассылает всем (с ограничением частоты, чтобы не заспамить).
    /// </summary>
    [RequireComponent(typeof(CatAudio))]
    public class CatVoice : NetworkBehaviour
    {
        private const float Cooldown = 0.45f;

        private CatAudio _audio;
        private float _nextLocal;
        private float _nextServer;

        /// <summary>Мини-игра сама слушает E/Q/R (ритм-игра) — голос по кнопкам выключен.</summary>
        public static bool Suppressed { get; set; }

        private void Awake() => _audio = GetComponent<CatAudio>();

        private void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard == null || CatInput.Blocked || Suppressed || Time.time < _nextLocal) return;

            int kind = -1;
            if (keyboard.eKey.wasPressedThisFrame) kind = 0;
            else if (keyboard.qKey.wasPressedThisFrame) kind = 1;
            else if (keyboard.rKey.wasPressedThisFrame) kind = 2;
            if (kind < 0) return;

            _nextLocal = Time.time + Cooldown;
            VoiceRpc((byte)kind);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void VoiceRpc(byte kind)
        {
            if (Time.time < _nextServer) return;
            _nextServer = Time.time + Cooldown * 0.8f;
            PlayVoiceRpc(kind);
        }

        [Rpc(SendTo.Everyone)]
        private void PlayVoiceRpc(byte kind) => _audio.Meow(kind);
    }
}
