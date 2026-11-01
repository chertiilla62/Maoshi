using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Кот двигается физикой, а не анимацией. Анимации AnimX содержат смещение корневой кости (root motion);
    /// если его не «поглотить», модель уезжает вперёд и дёргается в начале каждого цикла.
    /// Наличие OnAnimatorMove говорит Unity: root motion обрабатываю сам — и мы его просто игнорируем.
    /// Должен висеть на том же объекте, что и Animator.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CatRootMotionSink : MonoBehaviour
    {
        private Animator _animator;

        /// <summary>С какой скоростью «шагает» текущий клип (м/с, по горизонтали) — чтобы подогнать шаг под настоящую скорость.</summary>
        public float AnimSpeed { get; private set; }

        private void Awake() => _animator = GetComponent<Animator>();

        private void OnAnimatorMove()
        {
            // Позиция кота — от Rigidbody; смещение клипа только измеряем.
            if (_animator == null || Time.deltaTime <= 0f) return;
            Vector3 d = _animator.deltaPosition;
            float speed = new Vector2(d.x, d.z).magnitude / Time.deltaTime;
            AnimSpeed = Mathf.Lerp(AnimSpeed, speed, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }
    }
}
