using UnityEngine;

namespace Maoshi.MiniGames.PondFishing
{
    /// <summary>Пойманная рыба выпрыгивает из воды дугой к коту, кувыркается и исчезает. Только картинка.</summary>
    public class FishJump : MonoBehaviour
    {
        private Vector3 _from;
        private Transform _target;
        private float _t;
        private const float Flight = 0.55f;

        public static void Launch(GameObject template, Material material, Vector3 from, Transform target, float scale)
        {
            var go = Instantiate(template);
            go.SetActive(true);
            go.transform.position = from;
            go.transform.localScale = template.transform.lossyScale * (scale / 0.5f);
            if (material != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = material;
                    r.sharedMaterials = ms;
                }
            var jump = go.AddComponent<FishJump>();
            jump._from = from;
            jump._target = target;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Flight);
            Vector3 to = _target != null ? _target.position + Vector3.up * 0.5f : _from;
            transform.position = Vector3.Lerp(_from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.4f;
            transform.Rotate(new Vector3(0f, 360f, 540f) * Time.deltaTime, Space.Self);
            if (k >= 1f) Destroy(gameObject); // долетела до кота — «в зубы»
        }
    }
}
