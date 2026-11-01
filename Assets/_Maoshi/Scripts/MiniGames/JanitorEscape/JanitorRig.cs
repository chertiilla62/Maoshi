using UnityEngine;

namespace Maoshi.MiniGames.JanitorEscape
{
    /// <summary>
    /// «Живой» дворник поверх клипа бега: обе руки держат черенок метлы (двухзвенная IK),
    /// корпус закручивается на замахе и складывается при ударе, на радостях — потрясает метлой.
    /// Ноги и бег остаются из анимации. Только визуал, у каждого игрока свой.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class JanitorRig : MonoBehaviour
    {
        [SerializeField] private Janitor janitor;
        [SerializeField] private Transform broom;
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform upperArmR, foreArmR, handR;
        [SerializeField] private Transform upperArmL, foreArmL, handL;

        [Tooltip("Где на черенке руки (метры от верхнего конца, черенок идёт по локальной оси Z метлы).")]
        [SerializeField] private float gripRight = 0.12f;
        [SerializeField] private float gripLeft = 0.58f;

        private Janitor.JanitorState _lastState;
        private float _twist;
        private float _bend;

        // Клип бега пишет не все кости каждый кадр (и не пишет, когда аниматор отсечён камерой).
        // Если кость осталась такой, какой мы её оставили, — возвращаем исходную позу, иначе поворот копится.
        private Transform[] _bones;
        private Quaternion[] _base;
        private Quaternion[] _written;

        private void CacheBones()
        {
            _bones = new[] { spine, chest, upperArmR, foreArmR, handR, upperArmL, foreArmL, handL };
            _base = new Quaternion[_bones.Length];
            _written = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _base[i] = _written[i] = _bones[i].localRotation;
        }

        private void RestoreUntouched()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                if (Quaternion.Angle(bone.localRotation, _written[i]) < 0.01f) bone.localRotation = _base[i];
            }
        }

        private void RememberWritten()
        {
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _written[i] = _bones[i].localRotation;
        }

        private void LateUpdate()
        {
            if (janitor == null || broom == null) return;
            var state = janitor.State.Value;
            if (state != _lastState)
            {
                // Взмах метлой — свист, на радостях — «ха».
                if (state == Janitor.JanitorState.Recover) Maoshi.Audio.AudioManager.PlayAt("broom", broom.position, 1f, 0.08f, 35f);
                _lastState = state;
            }
            Apply(state, janitor.StateTime, Time.deltaTime);
        }

        /// <summary>Поставить позу для состояния (dt = 0 — сразу, без сглаживания; для превью в редакторе).</summary>
        public void Apply(Janitor.JanitorState state, float t, float dt)
        {
            if (janitor == null || broom == null) return;
            if (_bones == null) CacheBones();
            RestoreUntouched();

            // Корпус: поворот и наклон в зависимости от фазы удара.
            float twist = 0f, bend = 4f;
            switch (state)
            {
                case Janitor.JanitorState.Windup:
                    twist = 35f; bend = -8f; break;
                case Janitor.JanitorState.Recover:
                    twist = t < 0.15f ? -40f : Mathf.Lerp(-40f, 0f, (t - 0.15f) / 0.4f);
                    bend = t < 0.15f ? 24f : Mathf.Lerp(24f, 4f, (t - 0.15f) / 0.4f);
                    break;
                case Janitor.JanitorState.Gloat:
                    twist = t < 0.35f ? -40f : Mathf.Sin(t * 10f) * 12f;
                    bend = t < 0.35f ? 24f : -6f;
                    break;
                case Janitor.JanitorState.Chase:
                    bend = 10f; break; // бежит, подавшись вперёд
            }
            float sharp = state == Janitor.JanitorState.Recover || state == Janitor.JanitorState.Gloat ? 30f : 10f;
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-sharp * dt);
            _twist = Mathf.Lerp(_twist, twist, k);
            _bend = Mathf.Lerp(_bend, bend, k);

            Transform root = janitor.transform;
            if (spine != null)
                spine.rotation = Quaternion.AngleAxis(_twist * 0.4f, root.up) * Quaternion.AngleAxis(_bend * 0.5f, root.right) * spine.rotation;
            if (chest != null)
                chest.rotation = Quaternion.AngleAxis(_twist * 0.6f, root.up) * Quaternion.AngleAxis(_bend * 0.5f, root.right) * chest.rotation;

            // Руки на черенок.
            Vector3 gripR = broom.TransformPoint(new Vector3(0f, 0f, gripRight));
            Vector3 gripL = broom.TransformPoint(new Vector3(0f, 0f, gripLeft));
            Vector3 down = -root.up, back = -root.forward;
            if (upperArmR != null && foreArmR != null && handR != null)
                Solve(upperArmR, foreArmR, handR, gripR, upperArmR.position + root.right * 0.35f + down * 0.4f + back * 0.25f);
            if (upperArmL != null && foreArmL != null && handL != null)
                Solve(upperArmL, foreArmL, handL, gripL, upperArmL.position - root.right * 0.35f + down * 0.4f + back * 0.25f);
            RememberWritten();
        }

        /// <summary>Двухзвенная IK: плечо → предплечье → кисть тянется к цели, локоть смотрит в сторону pole.</summary>
        private static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
            float la = (bPos - aPos).magnitude, lb = (cPos - bPos).magnitude;
            if (la < 0.0001f || lb < 0.0001f) return;

            Vector3 toTarget = target - aPos;
            float d = Mathf.Clamp(toTarget.magnitude, 0.001f, (la + lb) * 0.999f);
            Vector3 dir = toTarget.normalized;

            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float angleA = Mathf.Acos(cosA);
            Vector3 bend = Vector3.ProjectOnPlane(pole - aPos, dir);
            if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            Vector3 bDesired = aPos + (dir * Mathf.Cos(angleA) + bend * Mathf.Sin(angleA)) * la;

            a.rotation = Quaternion.FromToRotation(bPos - aPos, bDesired - aPos) * a.rotation;
            bPos = b.position;
            cPos = c.position;
            b.rotation = Quaternion.FromToRotation(cPos - bPos, target - bPos) * b.rotation;
        }
    }
}
