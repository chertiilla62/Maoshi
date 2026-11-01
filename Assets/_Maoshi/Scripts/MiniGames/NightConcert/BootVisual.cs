using Maoshi.Audio;
using UnityEngine;

namespace Maoshi.MiniGames.NightConcert
{
    /// <summary>Ботинок из окна: летит дугой к коту, кувыркаясь, и отскакивает. Только картинка, у каждого своя.</summary>
    public class BootVisual : MonoBehaviour
    {
        private Vector3 _from;
        private Transform _target;
        private float _flight;
        private float _t;
        private Vector3 _spin;
        private bool _landed;
        private Vector3 _bounce;

        private static Material _leather;
        private static Material _sole;

        public static BootVisual Launch(Vector3 from, Transform target, float flight)
        {
            var root = new GameObject("Boot");
            root.transform.position = from;
            if (_leather == null) _leather = MakeMaterial(new Color(0.36f, 0.22f, 0.14f));
            if (_sole == null) _sole = MakeMaterial(new Color(0.12f, 0.1f, 0.1f));
            Part(root.transform, new Vector3(0f, 0.09f, 0.04f), new Vector3(0.16f, 0.12f, 0.34f), _leather); // носок
            Part(root.transform, new Vector3(0f, 0.2f, -0.08f), new Vector3(0.15f, 0.26f, 0.13f), _leather); // голенище
            Part(root.transform, new Vector3(0f, 0.015f, 0.02f), new Vector3(0.17f, 0.03f, 0.38f), _sole);
            var boot = root.AddComponent<BootVisual>();
            boot._from = from;
            boot._target = target;
            boot._flight = flight;
            boot._spin = new Vector3(Random.Range(500f, 800f), Random.Range(-200f, 200f), 0f);
            return boot;
        }

        private static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Maoshi/Toon");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static void Part(Transform parent, Vector3 local, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void Update()
        {
            if (!_landed)
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / _flight);
                Vector3 to = _target != null ? _target.position + Vector3.up * 0.5f : _from + Vector3.down * 5f;
                Vector3 p = Vector3.Lerp(_from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.5f;
                transform.position = p;
                transform.Rotate(_spin * Time.deltaTime, Space.Self);
                if (k >= 1f)
                {
                    _landed = true;
                    AudioManager.PlayAt("hit", to, 1f, 0.1f, 40f);
                    _bounce = new Vector3(Random.Range(-1.5f, 1.5f), 3f, Random.Range(-2.5f, -1f));
                }
                return;
            }

            // Отскок и падение на асфальт.
            _bounce += Vector3.down * 15f * Time.deltaTime;
            transform.position += _bounce * Time.deltaTime;
            transform.Rotate(_spin * 0.5f * Time.deltaTime, Space.Self);
            if (transform.position.y < 0.02f)
            {
                transform.position = new Vector3(transform.position.x, 0.02f, transform.position.z);
                _bounce = Vector3.zero;
                _spin = Vector3.zero;
                Destroy(gameObject, 2.5f);
                enabled = false;
            }
        }
    }
}
