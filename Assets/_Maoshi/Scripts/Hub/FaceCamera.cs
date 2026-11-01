using UnityEngine;

namespace Maoshi.Hub
{
    /// <summary>Надпись всегда повёрнута к камере (таблички зон во дворе).</summary>
    public class FaceCamera : MonoBehaviour
    {
        private Camera _camera;

        private void LateUpdate()
        {
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return;
            Vector3 away = transform.position - _camera.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
