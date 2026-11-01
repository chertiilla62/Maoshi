using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Мультяшные звёздочки, которые кружатся над головой оглушённого кота.
    /// Видны у всех: читают CatController.State (сетевая переменная). Меш звезды строится в коде, без ассетов.
    /// Вешается на кота сам (CatController.OnNetworkSpawn).
    /// </summary>
    public class StunStars : MonoBehaviour
    {
        private const int StarCount = 5;
        private const float Radius = 0.26f;
        private const float Height = 0.22f;
        private const float Spin = 300f;

        private static Mesh _starMesh;
        private static Material _material;

        private CatController _cat;
        private Transform _head;
        private Transform _root;
        private readonly Transform[] _stars = new Transform[StarCount];
        private float _show;
        private float _angle;

        private void Start()
        {
            _cat = GetComponent<CatController>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") { _head = t; break; }
            }

            _root = new GameObject("StunStars").transform;
            _root.SetParent(transform, false);
            for (int i = 0; i < StarCount; i++)
            {
                var star = new GameObject("Star");
                star.transform.SetParent(_root, false);
                star.AddComponent<MeshFilter>().sharedMesh = StarMesh();
                var renderer = star.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = StarMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _stars[i] = star.transform;
            }
            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_cat == null || _root == null) return;
            bool stunned = _cat.State.Value == CatState.Knocked;
            _show = Mathf.MoveTowards(_show, stunned ? 1f : 0f, Time.deltaTime * (stunned ? 8f : 4f));
            bool visible = _show > 0.01f;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            // Над макушкой: от кости головы, если нашли, иначе — над центром кота.
            Vector3 top = _head != null ? _head.position + Vector3.up * Height : transform.position + Vector3.up * 0.75f;
            _root.position = top;
            _root.rotation = Quaternion.identity;

            _angle += Spin * Time.deltaTime;
            var cam = Camera.main;
            for (int i = 0; i < StarCount; i++)
            {
                float a = (_angle + i * 360f / StarCount) * Mathf.Deg2Rad;
                // Кольцо чуть наклонено и «дышит» — как в мультиках.
                Vector3 p = new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a * 2f + i) * 0.03f + Mathf.Sin(a) * 0.05f, Mathf.Sin(a) * Radius);
                var star = _stars[i];
                star.localPosition = p;
                if (cam != null) star.rotation = Quaternion.LookRotation(star.position - cam.transform.position) * Quaternion.Euler(0f, 0f, _angle * 1.5f + i * 40f);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 12f + i);
                star.localScale = Vector3.one * (0.1f * _show * pulse);
            }
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }

        /// <summary>Плоская пятиконечная звезда (двусторонняя), радиус 1.</summary>
        private static Mesh StarMesh()
        {
            if (_starMesh != null) return _starMesh;
            var vertices = new Vector3[11];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? 1f : 0.45f;
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            var triangles = new int[10 * 3 * 2];
            for (int i = 0; i < 10; i++)
            {
                int a = i + 1, b = (i + 1) % 10 + 1;
                triangles[i * 6 + 0] = 0; triangles[i * 6 + 1] = a; triangles[i * 6 + 2] = b;
                triangles[i * 6 + 3] = 0; triangles[i * 6 + 4] = b; triangles[i * 6 + 5] = a;
            }
            _starMesh = new Mesh { name = "StunStar", vertices = vertices, triangles = triangles };
            _starMesh.RecalculateNormals();
            _starMesh.RecalculateBounds();
            return _starMesh;
        }

        private static Material StarMaterial()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _material = new Material(shader) { name = "StunStar" };
            var color = new Color(1f, 0.86f, 0.22f);
            _material.SetColor("_BaseColor", color);
            _material.color = color;
            _material.SetFloat("_Cull", 0f);
            return _material;
        }
    }
}
