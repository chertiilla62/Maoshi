using Maoshi.Settings;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.UI
{
    /// <summary>
    /// Счётчик FPS и пинга в правом верхнем углу. Включается в настройках («Показывать FPS и пинг»)
    /// или клавишей F3. Сохраняется в настройки.
    /// </summary>
    public class NetStatsOverlay : MonoBehaviour
    {
        private float _fps;
        private GUIStyle _style;

        private void Update()
        {
            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard != null)
            {
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    GameSettings.ShowNetStats = !GameSettings.ShowNetStats;
                    GameSettings.ApplyAndSave();
                }
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);
        }

        private void OnGUI()
        {
            if (!GameSettings.ShowNetStats) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };

            var manager = NetworkManager.Singleton;
            string rtt = "";
            if (manager != null && manager.IsConnectedClient && !manager.IsServer)
                rtt = $"   пинг {manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId)} мс";
            else if (manager != null && manager.IsServer)
                rtt = "   хост";

            GUI.Label(new Rect(Screen.width - 260, 4, 252, 22), $"FPS {_fps:F0}{rtt}", _style);
        }
    }
}
