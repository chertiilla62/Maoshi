using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using UnityEngine;

namespace Maoshi.MiniGames.HotPie
{
    /// <summary>
    /// Картинка пирожка (у всех): летит от бабушки или от прошлого держателя дугой, висит над головой
    /// держателя, краснеет, парит и дрожит, тикает в последние секунды; «пуф» — облако муки.
    /// Плюс бабушка у лотка поворачивается к тому, кому кидает.
    /// </summary>
    public class HotPieVisual : MonoBehaviour
    {
        [SerializeField] private Transform pie;
        [SerializeField] private Renderer[] pieRenderers;
        [SerializeField] private Transform grandma;
        [SerializeField] private Transform grandmaHand;
        [SerializeField] private Material steam;
        [SerializeField] private Material flour;
        [SerializeField] private Mesh puffMesh;
        [SerializeField] private Color baked = new(0.86f, 0.58f, 0.28f);
        [SerializeField] private Color hot = new(0.95f, 0.22f, 0.12f);

        private MaterialPropertyBlock _block;
        private int _explodeSerial;
        private float _nextTick, _steamTimer;
        private float _grandmaYaw;

        private class Puff
        {
            public Transform T;
            public Renderer R;
            public Vector3 Velocity;
            public float Life, MaxLife, Grow;
            public bool Active;
        }

        private readonly List<Puff> _puffs = new();

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            for (int i = 0; i < 70; i++) _puffs.Add(CreatePuff());
            if (grandma != null) _grandmaYaw = grandma.eulerAngles.y;
        }

        private Puff CreatePuff()
        {
            var go = new GameObject("Puff");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = puffMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return new Puff { T = go.transform, R = r };
        }

        private void Spawn(Material material, Vector3 position, Vector3 velocity, float life, float size, float grow)
        {
            foreach (var p in _puffs)
            {
                if (p.Active) continue;
                p.Active = true;
                p.T.gameObject.SetActive(true);
                p.R.sharedMaterial = material;
                p.T.position = position;
                p.T.localScale = Vector3.one * size;
                p.Velocity = velocity;
                p.Life = p.MaxLife = life;
                p.Grow = grow;
                return;
            }
        }

        private static Vector3 HeadOf(ulong clientId, out bool found)
        {
            var cat = CatController.FindByOwner(clientId);
            found = cat != null;
            return found ? cat.transform.position + Vector3.up * 1.25f : Vector3.zero;
        }

        private void Update()
        {
            UpdatePuffs();
            var mode = HotPieMode.Instance;
            if (mode == null || !mode.IsSpawned || pie == null)
            {
                if (pie != null) pie.gameObject.SetActive(false);
                return;
            }

            // Взрыв.
            if (mode.ExplodeSerial.Value != _explodeSerial)
            {
                _explodeSerial = mode.ExplodeSerial.Value;
                Vector3 at = HeadOf(mode.Exploded.Value, out bool ok);
                if (!ok) at = pie.position;
                AudioManager.PlayAt("poof", at, 1f);
                for (int i = 0; i < 26; i++)
                {
                    Vector3 dir = Random.onUnitSphere;
                    dir.y = Mathf.Abs(dir.y) * 0.8f + 0.1f;
                    Spawn(flour, at + dir * 0.2f, dir * Random.Range(1.5f, 3.2f), Random.Range(0.7f, 1.2f), Random.Range(0.25f, 0.45f), 2.2f);
                }
                // Крошки пирожка.
                for (int i = 0; i < 8; i++)
                    Spawn(steam, at, Random.onUnitSphere * 2.5f + Vector3.up * 2f, 0.8f, 0.08f, -0.05f);
            }

            ulong holder = mode.Holder.Value;
            bool active = holder != HotPieMode.Nobody;
            if (pie.gameObject.activeSelf != active) pie.gameObject.SetActive(active);

            // Бабушка смотрит на того, кому кидает (и на держателя вообще).
            if (grandma != null)
            {
                float targetYaw = _grandmaYaw;
                if (active)
                {
                    Vector3 h = HeadOf(holder, out bool okH);
                    if (okH)
                    {
                        Vector3 to = h - grandma.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 0.01f) targetYaw = Quaternion.LookRotation(to).eulerAngles.y;
                    }
                }
                float yaw = Mathf.LerpAngle(grandma.eulerAngles.y, targetYaw, 1f - Mathf.Exp(-4f * Time.deltaTime));
                grandma.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (!active) return;
            double now = mode.NowTime;

            // Полёт: от бабушки или от прошлого держателя — дугой.
            Vector3 target = HeadOf(holder, out bool okT);
            if (!okT) return;
            float heat = mode.Heat;
            float bob = Mathf.Sin(Time.time * 4f) * 0.05f;
            Vector3 position = target + Vector3.up * bob;
            double arrive = mode.ArriveTime.Value;
            if (now < arrive)
            {
                ulong from = mode.PreviousHolder.Value;
                Vector3 start;
                float flight;
                if (from == HotPieMode.Nobody) { start = grandmaHand != null ? grandmaHand.position : transform.position; flight = 0.7f; }
                else { start = HeadOf(from, out bool okF); if (!okF) start = target; flight = 0.25f; }
                float u = Mathf.Clamp01(1f - (float)(arrive - now) / flight);
                position = Vector3.Lerp(start, target, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * (from == HotPieMode.Nobody ? 2.2f : 0.8f));
            }

            // Дрожь в конце, вращение.
            bool critical = mode.Critical;
            if (critical) position += Random.insideUnitSphere * 0.04f;
            pie.position = position;
            pie.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 3f) * 8f, Time.time * (critical ? 360f : 90f), 0f);
            float scale = 1f + (critical ? 0.12f * Mathf.Abs(Mathf.Sin(Time.time * 14f)) : 0.04f * Mathf.Sin(Time.time * 5f));
            pie.localScale = Vector3.one * scale;

            // Цвет: золотистый → красный.
            Color c = Color.Lerp(baked, hot, Mathf.SmoothStep(0f, 1f, heat));
            if (critical) c = Color.Lerp(c, Color.white, 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 14f)));
            _block.SetColor("_BaseColor", c);
            _block.SetColor("_Color", c);
            foreach (var r in pieRenderers) if (r != null) r.SetPropertyBlock(_block);

            // Пар: чем горячее, тем гуще.
            _steamTimer -= Time.deltaTime;
            if (_steamTimer <= 0f)
            {
                _steamTimer = Mathf.Lerp(0.22f, 0.04f, heat);
                Spawn(steam, position + Random.insideUnitSphere * 0.08f + Vector3.up * 0.1f,
                    Vector3.up * Random.Range(0.5f, 0.9f) + Random.insideUnitSphere * 0.15f, 0.8f, 0.035f + heat * 0.03f, 0.7f);
            }

            // Тиканье — чаще к концу.
            if (critical && Time.time >= _nextTick)
            {
                _nextTick = Time.time + 0.25f;
                AudioManager.PlayAt("tick", position, 0.7f);
            }
            else if (!critical && heat > 0.5f && Time.time >= _nextTick)
            {
                _nextTick = Time.time + Mathf.Lerp(0.9f, 0.5f, heat);
                AudioManager.PlayAt("tick", position, 0.35f);
            }
        }

        private void UpdatePuffs()
        {
            float dt = Time.deltaTime;
            foreach (var p in _puffs)
            {
                if (!p.Active) continue;
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    p.Active = false;
                    p.T.gameObject.SetActive(false);
                    continue;
                }
                p.Velocity *= 1f - 2.2f * dt;
                p.T.position += p.Velocity * dt;
                p.T.localScale *= 1f + p.Grow * dt;
                float k = p.Life / p.MaxLife;
                if (k < 0.3f) p.T.localScale *= 1f - 3f * dt;
            }
        }
    }
}
