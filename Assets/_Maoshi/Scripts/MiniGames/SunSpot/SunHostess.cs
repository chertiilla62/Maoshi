using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.SunSpot
{
    /// <summary>
    /// Хозяйка с пульверизатором (только картинка, решает сервер в SunSpotMode):
    /// поворачивается к цели, поднимает бутылку, над головой «!»; под котом — синий круг-предупреждение;
    /// в момент пшика — струя капель дугой и брызги. Мокрые коты капают водой.
    /// </summary>
    public class SunHostess : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform bottlePivot;
        [SerializeField] private Transform nozzle;
        [SerializeField] private TextMesh sign;
        [SerializeField] private Transform ring;
        [SerializeField] private Material water;
        [SerializeField] private Mesh dropMesh;

        private int _serial;
        private float _yaw, _restYaw;
        private Quaternion _bottleRest;
        private bool _splashed;

        private class Drop
        {
            public Transform T;
            public Vector3 Velocity;
            public float Life;
            public bool Active;
        }

        private readonly List<Drop> _drops = new();
        private float _dripTimer;

        private void Awake()
        {
            if (body == null) body = transform;
            _restYaw = body.eulerAngles.y;
            _yaw = _restYaw;
            if (bottlePivot != null) _bottleRest = bottlePivot.localRotation;
            if (ring != null) ring.gameObject.SetActive(false);
            for (int i = 0; i < 60; i++) _drops.Add(CreateDrop());
        }

        private Drop CreateDrop()
        {
            var go = new GameObject("Drop");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = dropMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = water;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return new Drop { T = go.transform };
        }

        private Drop Spawn(Vector3 position, Vector3 velocity, float life, float size)
        {
            foreach (var d in _drops)
            {
                if (d.Active) continue;
                d.Active = true;
                d.T.gameObject.SetActive(true);
                d.T.position = position;
                d.T.localScale = Vector3.one * size;
                d.Velocity = velocity;
                d.Life = life;
                return d;
            }
            return null;
        }

        private void Update()
        {
            var mode = SunSpotMode.Instance;
            UpdateDrops();
            if (mode == null || !mode.IsSpawned) return;

            double now = mode.NowTime;
            double impact = mode.SprayImpact.Value;
            bool aiming = mode.SpraySerial.Value > 0 && now < impact + 0.5;
            float untilImpact = (float)(impact - now);

            if (mode.SpraySerial.Value != _serial)
            {
                _serial = mode.SpraySerial.Value;
                _splashed = false;
                AudioManager.PlayAt("hum", transform.position, 0.5f);
            }

            // Поворот к цели и бутылка вверх.
            Vector3 target = mode.SprayPoint.Value;
            float targetYaw = _restYaw;
            if (aiming)
            {
                Vector3 to = target - body.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) targetYaw = Quaternion.LookRotation(to).eulerAngles.y;
            }
            _yaw = Mathf.LerpAngle(_yaw, targetYaw, 1f - Mathf.Exp(-10f * Time.deltaTime));
            body.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (bottlePivot != null)
            {
                float raise = aiming ? 1f : 0f;
                bottlePivot.localRotation = Quaternion.Slerp(bottlePivot.localRotation, _bottleRest * Quaternion.Euler(-80f * raise, 0f, 0f), 1f - Mathf.Exp(-12f * Time.deltaTime));
            }

            // Знак над головой.
            if (sign != null)
            {
                string text = aiming && untilImpact > 0 ? "!" : "";
                if (sign.text != text) sign.text = text;
                sign.transform.position = body.position + Vector3.up * (2.2f + Mathf.Sin(Time.time * 5f) * 0.04f);
                var cam = Camera.main;
                if (cam != null) sign.transform.rotation = Quaternion.LookRotation(sign.transform.position - cam.transform.position);
            }

            // Круг-предупреждение у цели (на полу или мебели под ней).
            if (ring != null)
            {
                bool show = aiming && untilImpact > -0.15f;
                if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
                if (show)
                {
                    Vector3 p = target + Vector3.up * 1.5f;
                    if (mode.EnvironmentRaycast(p, Vector3.down, 4f, out var hit)) p = hit.point + Vector3.up * 0.02f;
                    else p = target + Vector3.up * 0.02f;
                    ring.position = p;
                    float k = Mathf.Clamp01(1f - untilImpact);
                    ring.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, k) * (1f + 0.05f * Mathf.Sin(Time.time * 18f));
                }
            }

            // Струя: за 0.35 с до пшика летят капли дугой; в момент — брызги.
            if (aiming && nozzle != null && untilImpact < 0.35f && untilImpact > 0f && Time.frameCount % 2 == 0)
            {
                Vector3 from = nozzle.position;
                Vector3 to = target + Vector3.up * 0.4f;
                float flight = Mathf.Max(0.05f, untilImpact);
                Vector3 v = (to - from) / flight - 0.5f * Physics.gravity * flight;
                Spawn(from, v + Random.insideUnitSphere * 0.4f, flight, 0.06f);
            }
            if (aiming && !_splashed && untilImpact <= 0f)
            {
                _splashed = true;
                AudioManager.PlayAt("spray", target, 0.9f);
                AudioManager.PlayAt("splash", target, 0.5f);
                for (int i = 0; i < 14; i++)
                {
                    Vector3 dir = Random.insideUnitSphere;
                    dir.y = Mathf.Abs(dir.y) + 0.6f;
                    Spawn(target + Vector3.up * 0.4f, dir * 2.4f, 0.6f, Random.Range(0.04f, 0.08f));
                }
            }

            // Мокрые коты капают.
            _dripTimer -= Time.deltaTime;
            if (_dripTimer <= 0f)
            {
                _dripTimer = 0.07f;
                foreach (ulong id in mode.WetCats)
                {
                    var cat = CatController.FindByOwner(id);
                    if (cat == null) continue;
                    Vector3 p = cat.transform.position + Vector3.up * Random.Range(0.35f, 0.6f) + Random.insideUnitSphere * 0.18f;
                    Spawn(p, Vector3.down * 0.5f, 0.45f, 0.035f);
                }
            }
        }

        private void UpdateDrops()
        {
            float dt = Time.deltaTime;
            foreach (var d in _drops)
            {
                if (!d.Active) continue;
                d.Life -= dt;
                if (d.Life <= 0f)
                {
                    d.Active = false;
                    d.T.gameObject.SetActive(false);
                    continue;
                }
                d.Velocity += Physics.gravity * dt;
                d.T.position += d.Velocity * dt;
            }
        }
    }
}
