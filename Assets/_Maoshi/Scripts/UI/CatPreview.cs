using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Maoshi.UI
{
    /// <summary>
    /// Кот на витрине главного меню: копия модели из префаба кота, своя камера рисует её в текстуру,
    /// текстура показывается в интерфейсе. Кот медленно поворачивается; окраска меняется сразу при выборе.
    /// Живёт, пока меню его «трогает» (Touch каждый кадр), иначе засыпает и не тратит кадр.
    /// </summary>
    public class CatPreview : MonoBehaviour
    {
        private static readonly Vector3 Origin = new(0f, -400f, 0f);

        public RenderTexture Texture { get; private set; }

        private CatAppearance _source;
        private Transform _model;
        private Camera _camera;
        private Light _light;
        private float _lastTouch;
        private int _skin = -2;
        private float _yaw = 150f;

        private static CatPreview _instance;

        /// <summary>Получить витрину (создаётся при первом обращении). null, если префаб кота не найден.</summary>
        public static CatPreview Get()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;
            // Обычный объект (без DontSave): Unity сам удаляет его при выходе из Play и закрытии игры.
            var go = new GameObject("CatPreview");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CatPreview>();
            if (!_instance.Build())
            {
                Destroy(go);
                _instance = null;
            }
            return _instance;
        }

        public void Touch() => _lastTouch = Time.unscaledTime;

        private readonly System.Collections.Generic.Dictionary<string, GameObject> _accessories = new();
        private bool _focusHead;

        /// <summary>Надеть аксессуары на кота витрины (индексы в CatAccessories, −1 — ничего).</summary>
        public void SetAccessories(int head, int neck, int face) => CatAccessories.ApplyTo(_model, _accessories, head, neck, face);

        /// <summary>Магазин: камера ближе к голове, кот поворачивается медленнее и чаще смотрит в камеру.</summary>
        public void FocusHead(bool on) => _focusHead = on;

        public void SetSkin(int skin)
        {
            if (skin == _skin || _source == null || _model == null) return;
            _skin = skin;
            _source.ApplySkinTo(_model, skin < 0 ? 0 : skin);
        }

        private bool Build()
        {
            _source = FindCatPrefab();
            if (_source == null || _source.Model == null) return false;

            _model = Instantiate(_source.Model, Origin, Quaternion.Euler(0f, _yaw, 0f), transform);
            _model.gameObject.SetActive(true);
            foreach (var behaviour in _model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Живые части аксессуаров (плащ, пропеллер, нимб) оставляем — они только рисуют.
                if (behaviour is CapeCloth || behaviour is AccessorySpin) continue;
                behaviour.enabled = false; // Animator — не MonoBehaviour, продолжает играть idle
            }

            Texture = new RenderTexture(640, 720, 24, RenderTextureFormat.ARGB32) { name = "CatPreviewRT", antiAliasing = 4 };

            var camGo = new GameObject("CatPreviewCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.fieldOfView = 26f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.targetTexture = Texture;
            _camera.transform.position = Origin + new Vector3(0f, 0.55f, 2.9f);
            _camera.transform.LookAt(Origin + new Vector3(0f, 0.32f, 0f));
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            // В меню может не быть солнца — своё, мягкое.
            var lightGo = new GameObject("CatPreviewLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.25f;
            _light.color = new Color(1f, 0.96f, 0.9f);
            _light.shadows = LightShadows.None;
            if (RenderSettings.sun != null) _light.enabled = false;

            _lastTouch = Time.unscaledTime;
            return true;
        }

        private static CatAppearance FindCatPrefab()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.NetworkConfig?.Prefabs == null) return null;
            foreach (var entry in manager.NetworkConfig.Prefabs.Prefabs)
            {
                if (entry?.Prefab == null) continue;
                var appearance = entry.Prefab.GetComponent<CatAppearance>();
                if (appearance != null) return appearance;
            }
            return null;
        }

        private void Update()
        {
            bool active = Time.unscaledTime - _lastTouch < 0.25f;
            if (_camera.enabled != active)
            {
                _camera.enabled = active;
                _model.gameObject.SetActive(active);
                if (RenderSettings.sun == null) _light.enabled = active;
            }
            if (!active) return;

            _yaw += Time.unscaledDeltaTime * 18f;
            // В магазине кот покачивается мордой к камере, чтобы аксессуары было видно.
            float yaw = _focusHead ? Mathf.Sin(Time.unscaledTime * 0.6f) * 28f : _yaw;
            _model.rotation = Quaternion.Euler(0f, yaw, 0f);
            // Голова кота (в позе модели — ~0.57 м над землёй и ~0.37 м вперёд): камера держит её в центре.
            Vector3 headPoint = Origin + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.57f, 0.37f);
            Vector3 camTarget = _focusHead ? headPoint + new Vector3(0f, 0.12f, 1.25f) : Origin + new Vector3(0f, 0.55f, 2.9f);
            Vector3 lookTarget = _focusHead ? headPoint + new Vector3(0f, -0.04f, 0f) : Origin + new Vector3(0f, 0.32f, 0f);
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, camTarget, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            _camera.transform.rotation = Quaternion.Slerp(_camera.transform.rotation, Quaternion.LookRotation(lookTarget - _camera.transform.position), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        }

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (Texture != null)
            {
                Texture.Release();
                Destroy(Texture);
            }
            if (_instance == this) _instance = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Уборка в редакторе: витрины и объёмы графики, оставшиеся от прошлых запусков Play
        /// (старая версия создавала их с DontSave, и они переживали выход из Play, рисуя котов поверх игры).
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookCleanup()
        {
            UnityEditor.EditorApplication.delayCall += CleanupStrays;
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) CleanupStrays();
            };
        }

        private static void CleanupStrays()
        {
            if (Application.isPlaying) return;
            int removed = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform.parent != null) continue;
                if (UnityEditor.EditorUtility.IsPersistent(go)) continue; // ассеты не трогаем
                if (go.name is "CatPreview" or "MaoshiGraphicsVolume")
                {
                    DestroyImmediate(go);
                    removed++;
                }
            }
            if (removed > 0) Debug.Log($"[Maoshi] Убраны остатки витрины кота из редактора: {removed}");
        }
#endif
    }
}
