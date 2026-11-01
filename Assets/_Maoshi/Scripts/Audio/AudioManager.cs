using System.Collections.Generic;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.Settings;
using UnityEngine;

namespace Maoshi.Audio
{
    /// <summary>
    /// Весь звук игры: эффекты (2D и в мире) и музыка с плавной сменой по состоянию игры.
    /// Создаётся сам при запуске и живёт между сценами. Звуки — Resources/Maoshi/Audio/Sfx, музыка — Resources/Maoshi/Audio/Music.
    /// Варианты одного звука называются name_1, name_2…: Play("meow") выбирает случайный, чтобы не надоедало.
    /// Громкость: общая (AudioListener), музыка и эффекты — из настроек.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private const int PoolSize = 24;
        private const float MusicGain = 0.55f;
        private const float FadeTime = 1.4f;

        private readonly Dictionary<string, List<AudioClip>> _sfx = new();
        private readonly Dictionary<string, AudioClip> _music = new();
        private readonly Dictionary<string, float> _lastPlayed = new();
        private AudioSource[] _pool;
        private int _next;
        private AudioSource _ui;

        private AudioSource _musicA, _musicB;
        private string _track = "";
        private float _fade = 1f;

        private AudioListener _ownListener;
        private float _listenerCheck;
        private float _noListenerSince = -1f;
        private NetworkPlayer _fishOwner;
        private int _lastFish = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("MaoshiAudio");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AudioManager>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            foreach (var clip in Resources.LoadAll<AudioClip>("Maoshi/Audio/Sfx"))
            {
                string key = BaseName(clip.name);
                if (!_sfx.TryGetValue(key, out var list)) _sfx[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var clip in Resources.LoadAll<AudioClip>("Maoshi/Audio/Music")) _music[clip.name] = clip;

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Sfx" + i);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3f;
                source.maxDistance = 30f;
                _pool[i] = source;
            }

            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;

            _musicA = MakeMusicSource();
            _musicB = MakeMusicSource();
            _ownListener = gameObject.AddComponent<AudioListener>();
            _ownListener.enabled = false;
        }

        private AudioSource MakeMusicSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        // ───────────────────────── Эффекты ─────────────────────────

        /// <summary>Звук «в ушах» (интерфейс, награды, отсчёт).</summary>
        public static void Play(string id, float volume = 1f, float pitchJitter = 0.03f) =>
            Instance?.PlayInternal(id, null, volume, pitchJitter, 0f);

        /// <summary>Звук в точке мира: громче рядом, тише вдали.</summary>
        public static void PlayAt(string id, Vector3 position, float volume = 1f, float pitchJitter = 0.06f, float maxDistance = 30f) =>
            Instance?.PlayInternal(id, position, volume, pitchJitter, maxDistance);

        /// <summary>Звук в точке мира с заданной высотой (поющие коты в «Ночном концерте»).</summary>
        public static void PlayAtPitch(string id, Vector3 position, float volume, float pitch, float maxDistance = 30f) =>
            Instance?.PlayInternal(id, position, volume, 0f, maxDistance, pitch);

        private void PlayInternal(string id, Vector3? position, float volume, float pitchJitter, float maxDistance, float basePitch = 1f)
        {
            if (!_sfx.TryGetValue(id, out var clips) || clips.Count == 0) return;
            // Один и тот же звук не чаще раза в 40 мс — от пачки одновременных событий не трещит.
            if (_lastPlayed.TryGetValue(id, out float last) && Time.unscaledTime - last < 0.04f && position == null) return;
            _lastPlayed[id] = Time.unscaledTime;

            var clip = clips[Random.Range(0, clips.Count)];
            float pitch = basePitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            float gain = Mathf.Clamp01(volume) * GameSettings.SfxVolume;
            if (gain <= 0.001f) return;

            if (position == null)
            {
                _ui.pitch = pitch;
                _ui.PlayOneShot(clip, gain);
                return;
            }

            var source = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            source.transform.position = position.Value;
            source.spatialBlend = 1f;
            source.maxDistance = maxDistance;
            source.pitch = pitch;
            source.volume = gain;
            source.clip = clip;
            source.Play();
        }

        // ───────────────────────── Музыка ─────────────────────────

        private void Update()
        {
            CheckListener();
            UpdateMusic();
            TrackFish();
        }

        private void UpdateMusic()
        {
            string wanted = PickTrack();
            if (wanted == "none" && _track != "none")
            {
                // Своя музыка у мини-игры — фоновая плавно затихает.
                (_musicA, _musicB) = (_musicB, _musicA);
                _musicA.Stop();
                _musicA.clip = null;
                _track = "none";
                _fade = 0f;
            }
            else if (wanted != _track && _music.TryGetValue(wanted, out var clip))
            {
                // Новая дорожка — во второй источник, старая уходит в затухание.
                (_musicA, _musicB) = (_musicB, _musicA);
                _musicA.clip = clip;
                _musicA.time = 0f;
                _musicA.Play();
                _track = wanted;
                _fade = 0f;
            }

            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / FadeTime);
            float target = GameSettings.MusicVolume * MusicGain;
            _musicA.volume = target * _fade;
            _musicB.volume = target * (1f - _fade);
            if (_fade >= 1f && _musicB.isPlaying) _musicB.Stop();
        }

        /// <summary>Какая музыка нужна сейчас: меню, двор, мини-игра, свалка или финал.</summary>
        private static string PickTrack()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null || connection.State != ConnectionState.Connected || connection.IsInMenu) return "menu";

            var session = SessionManager.Instance;
            if (session != null && session.Phase.Value == SessionPhase.Final) return "final";
            var brawl = Showdown.ShowdownManager.Instance;
            if (brawl != null && brawl.IsActive) return "brawl";
            if (MiniGameMode.Current is IMusicOverride custom) return custom.MusicTrack;
            if (MiniGameMode.Current != null) return "game";
            return "hub";
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        /// <summary>Своя рыба выросла — звук награды.</summary>
        private void TrackFish()
        {
            var local = NetworkPlayer.Local;
            if (local != _fishOwner)
            {
                _fishOwner = local;
                _lastFish = local != null ? local.Fish.Value : -1;
                return;
            }
            if (local == null) return;
            int fish = local.Fish.Value;
            if (_lastFish >= 0 && fish > _lastFish) Play(fish - _lastFish >= 5 ? "fish_big" : "fish", 0.8f, 0f);
            _lastFish = fish;
        }

        /// <summary>Если в сцене нет слушателя (меню без камеры), слушаем сами; есть свой у камеры — уступаем.</summary>
        private void CheckListener()
        {
            if (Time.unscaledTime < _listenerCheck) return;
            _listenerCheck = Time.unscaledTime + 0.2f;
            int others = 0;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener != _ownListener && listener.isActiveAndEnabled) others++;
            // Свой включаем, только если чужого нет уже полсекунды (при смене сцены камера появляется не сразу).
            if (others > 0) { _ownListener.enabled = false; _noListenerSince = -1f; }
            else if (!_ownListener.enabled)
            {
                if (_noListenerSince < 0f) _noListenerSince = Time.unscaledTime;
                else if (Time.unscaledTime - _noListenerSince > 0.5f) _ownListener.enabled = true;
            }
        }

        private static string BaseName(string name)
        {
            int underscore = name.LastIndexOf('_');
            if (underscore > 0 && int.TryParse(name.Substring(underscore + 1), out _)) return name.Substring(0, underscore);
            return name;
        }
    }
}
