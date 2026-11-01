using Maoshi.Audio;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.PondFishing
{
    /// <summary>
    /// Кочка в пруду. Раз в period секунд трясётся (предупреждение) и уходит под воду на пару секунд —
    /// кто стоял, тот в воде. Расписание считается у всех одинаково по сетевому времени от начала игры,
    /// поэтому кочку не нужно синхронизировать по сети: это обычный кинематический объект.
    /// Пока игра не началась (и первые секунды игры) кочки стоят.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PondStone : MonoBehaviour
    {
        [SerializeField] private float period = 15f;
        [SerializeField] private float phase;
        [SerializeField] private float shakeSeconds = 1.3f;
        [SerializeField] private float sinkSeconds = 1.8f;
        [SerializeField] private float sinkDepth = 1.0f;
        [SerializeField] private float calmStart = 6f;

        private Rigidbody _body;
        private Vector3 _rest;
        private Quaternion _restRotation;
        private bool _wasSunk;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _rest = transform.position;
            _restRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            var mode = MiniGameMode.Current as FishingMode;
            float offset = 0f;
            Quaternion tilt = Quaternion.identity;

            if (mode != null && mode.IsSpawned && mode.Phase == MiniGamePhase.Playing && mode.GameStart.Value > 0 && NetworkManager.Singleton != null)
            {
                double t = NetworkManager.Singleton.ServerTime.Time - mode.GameStart.Value - calmStart;
                if (t > 0)
                {
                    float c = (float)((t + phase) % period);
                    float shakeAt = period - sinkSeconds - shakeSeconds;
                    float sinkAt = period - sinkSeconds;
                    if (c >= shakeAt && c < sinkAt)
                    {
                        float k = (c - shakeAt) / shakeSeconds;
                        tilt = Quaternion.Euler(Mathf.Sin(c * 31f) * 7f * k, 0f, Mathf.Cos(c * 27f) * 7f * k);
                        offset = -0.05f * k;
                    }
                    else if (c >= sinkAt)
                    {
                        float s = c - sinkAt;
                        float down = Mathf.Clamp01(s / 0.25f) * Mathf.Clamp01((sinkSeconds - s) / 0.35f);
                        offset = -sinkDepth * down;
                    }
                }
            }

            bool sunk = offset < -0.5f;
            if (sunk && !_wasSunk) AudioManager.PlayAt("splash", _rest, 0.6f, 0.1f, 25f);
            _wasSunk = sunk;

            _body.MovePosition(_rest + Vector3.up * offset);
            _body.MoveRotation(_restRotation * tilt);
        }
    }
}
