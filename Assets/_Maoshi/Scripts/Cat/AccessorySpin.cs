using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>Крутит/покачивает часть аксессуара (пропеллер на шапке, нимб). Вешается на объект в префабе кота.</summary>
    public class AccessorySpin : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField] private float degreesPerSecond = 720f;
        [SerializeField] private float bobHeight;
        [SerializeField] private float bobSpeed = 3f;

        private Quaternion _rest;
        private Vector3 _restPosition;

        private void Awake()
        {
            _rest = transform.localRotation;
            _restPosition = transform.localPosition;
        }

        private void Update()
        {
            float t = Time.time;
            transform.localRotation = _rest * Quaternion.AngleAxis(t * degreesPerSecond, axis);
            if (bobHeight > 0f)
                transform.position = transform.parent.TransformPoint(_restPosition) + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobHeight);
        }
    }
}
