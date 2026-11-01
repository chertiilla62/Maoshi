using System;
using System.Collections.Generic;
using System.Globalization;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.Settings;
using Maoshi.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Maoshi.MiniGames.NightConcert
{
    /// <summary>Ботинки и серия игрока (видны всем — для подписи «у кого ботинок»).</summary>
    public struct ConcertPlayer : INetworkSerializable, IEquatable<ConcertPlayer>
    {
        public ulong ClientId;
        public int Combo;
        public int Boots;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Combo);
            serializer.SerializeValue(ref Boots);
        }

        public bool Equals(ConcertPlayer other) => ClientId == other.ClientId && Combo == other.Combo && Boots == other.Boots;
    }

    /// <summary>
    /// «Ночной концерт» — ритм-игра под окнами. Играет аккомпанемент, по дорожке едут ноты:
    /// E — «мяу» (кот поёт мелодию нужной высоты), Q — «шшш». Попал в такт — очки и серия, все слышат твой голос.
    /// Каждые 12 нот подряд — ботинок (до двух). R — жилец кидает ботинок из окна в лидера (или во второго, если лидер ты):
    /// он падает и 2 секунды не может петь.
    ///
    /// Время: у всех одна точка старта песни (сервер), музыка ставится на неё через AudioSettings.dspTime,
    /// попадание каждый судит сам по своему звуку (без пинга), сервер считает очки, серии и ботинки.
    /// </summary>
    public class ConcertMode : MiniGameMode, IMiniGameModal, IMusicOverride
    {
        private const float Perfect = 0.07f;
        private const float Good = 0.15f;
        private const float LeadIn = 2.5f;
        private const float Lookahead = 2.2f;
        private const float StunSeconds = 2f;
        private const float BootFlight = 0.8f;

        [Header("Ночной концерт")]
        [SerializeField] private TextAsset chart;
        [SerializeField] private AudioClip music;
        [Tooltip("Окна, откуда жильцы кидают ботинки.")]
        [SerializeField] private Transform[] windows;
        [SerializeField] private int comboPerBoot = 12;
        [SerializeField] private int maxBoots = 2;

        public readonly NetworkVariable<double> SongStart = new(-1);
        public NetworkList<ConcertPlayer> Players;

        public string MusicTrack => "none";

        private struct Note
        {
            public float Time;
            public byte Lane;
            public int Semitone;
        }

        private readonly List<Note> _notes = new();
        private float _songLength;

        // Сервер.
        private readonly Dictionary<ulong, HashSet<int>> _judged = new();
        private readonly List<(double at, ulong target)> _pendingBoots = new();

        // Клиент.
        private AudioSource _source;
        private double _dspStart = -1;
        private int _nextMissCheck;
        private readonly Dictionary<int, byte> _myResults = new(); // 0 мимо, 1 хорошо, 2 отлично
        private double _stunUntil = -1;
        private int _localCombo;

        // Интерфейс дорожки.
        private VisualElement _track;
        private VisualElement _notesLayer;
        private Label _judgement;
        private Label _combo;
        private Label _boots;
        private float _judgementShownAt = -10f;
        private readonly Dictionary<int, VisualElement> _noteViews = new();

        protected override bool UsesScores => true;

        protected override void Awake()
        {
            base.Awake();
            Players = new NetworkList<ConcertPlayer>();
            ParseChart();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CatVoice.Suppressed = true;
        }

        public override void OnNetworkDespawn()
        {
            CatVoice.Suppressed = false;
            if (_source != null) _source.Stop();
            base.OnNetworkDespawn();
        }

        private void ParseChart()
        {
            _notes.Clear();
            if (chart == null) return;
            float bpm = 96f;
            int bars = 0;
            foreach (var raw in chart.text.Split('\n'))
            {
                var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] == "bpm") { bpm = float.Parse(parts[1], CultureInfo.InvariantCulture); continue; }
                if (parts[0] == "bars") { bars = int.Parse(parts[1], CultureInfo.InvariantCulture); continue; }
                if (parts.Length < 3) continue;
                float beat = float.Parse(parts[0], CultureInfo.InvariantCulture);
                _notes.Add(new Note
                {
                    Time = beat * 60f / bpm,
                    Lane = byte.Parse(parts[1], CultureInfo.InvariantCulture),
                    Semitone = int.Parse(parts[2], CultureInfo.InvariantCulture),
                });
            }
            _notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            _songLength = music != null ? music.length : bars * 4 * 60f / bpm;
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            Players.Clear();
            _judged.Clear();
            foreach (var id in Participants)
            {
                Players.Add(new ConcertPlayer { ClientId = id });
                _judged[id] = new HashSet<int>();
            }
        }

        protected override void OnGameStarted()
        {
            SongStart.Value = Now + LeadIn;
            PhaseEndTime.Value = SongStart.Value + _songLength;
        }

        protected override void OnGameTick()
        {
            for (int i = _pendingBoots.Count - 1; i >= 0; i--)
            {
                if (Now < _pendingBoots[i].at) continue;
                ulong target = _pendingBoots[i].target;
                _pendingBoots.RemoveAt(i);
                var cat = CatController.FindByOwner(target);
                if (cat != null) cat.ReceiveHitRpc(-cat.transform.forward * 1.5f + Vector3.up * 2f, true);
                StunRpc(target);
                SetCombo(target, 0, false);
            }

            if (SongStart.Value > 0 && Now >= SongStart.Value + _songLength + 0.5) FinishGame();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResultRpc(int noteIndex, byte result, RpcParams rpcParams = default)
        {
            ulong id = rpcParams.Receive.SenderClientId;
            if (Phase != MiniGamePhase.Playing || noteIndex < 0 || noteIndex >= _notes.Count) return;
            if (!_judged.TryGetValue(id, out var judged) || !judged.Add(noteIndex)) return;

            int index = PlayerIndex(id);
            if (index < 0) return;
            var player = Players[index];

            if (result == 0)
            {
                SetCombo(id, 0, false);
                return;
            }

            int combo = player.Combo + 1;
            // Очки небольшие, чтобы влезали в табло: отлично 10, хорошо 6, за серию до +5.
            int points = (result == 2 ? 10 : 6) + Mathf.Min(5, combo / 10);
            AddScore(id, points);
            SetCombo(id, combo, combo % comboPerBoot == 0);
            var note = _notes[noteIndex];
            SingRpc(id, note.Lane, note.Semitone);
        }

        private void SetCombo(ulong id, int combo, bool earnBoot)
        {
            int index = PlayerIndex(id);
            if (index < 0) return;
            var player = Players[index];
            player.Combo = combo;
            if (earnBoot && player.Boots < maxBoots) player.Boots++;
            Players[index] = player;
        }

        private int PlayerIndex(ulong id)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].ClientId == id) return i;
            return -1;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ThrowRpc(RpcParams rpcParams = default)
        {
            ulong id = rpcParams.Receive.SenderClientId;
            if (Phase != MiniGamePhase.Playing) return;
            int index = PlayerIndex(id);
            if (index < 0 || Players[index].Boots <= 0) return;

            // Цель — лидер, а если лидер ты — следующий за тобой.
            ulong target = ulong.MaxValue;
            int best = -1;
            foreach (var entry in Scores)
            {
                if (entry.ClientId == id) continue;
                if (entry.Score > best) { best = entry.Score; target = entry.ClientId; }
            }
            if (target == ulong.MaxValue) return;

            var player = Players[index];
            player.Boots--;
            Players[index] = player;

            int window = windows != null && windows.Length > 0 ? Random.Range(0, windows.Length) : -1;
            _pendingBoots.Add((Now + BootFlight, target));
            BootRpc(id, target, window);
        }

        // ───────────────────────── Сеть: всем ─────────────────────────

        [Rpc(SendTo.Everyone)]
        private void SingRpc(ulong clientId, byte lane, int semitone)
        {
            if (NetworkManager != null && clientId == NetworkManager.LocalClientId) return; // свой голос уже звучал
            PlayVoice(clientId, lane, semitone);
        }

        [Rpc(SendTo.Everyone)]
        private void StunRpc(ulong target)
        {
            if (NetworkManager == null || target != NetworkManager.LocalClientId) return;
            _stunUntil = SongTime + StunSeconds;
            _localCombo = 0;
            ShowJudgement("Ботинком!", new Color(0.9f, 0.3f, 0.25f));
        }

        [Rpc(SendTo.Everyone)]
        private void BootRpc(ulong thrower, ulong target, int window)
        {
            var cat = CatController.FindByOwner(target);
            if (cat == null) return;
            Vector3 from = window >= 0 && windows != null && window < windows.Length && windows[window] != null
                ? windows[window].position
                : cat.transform.position + Vector3.up * 8f;
            var boot = BootVisual.Launch(from, cat.transform, BootFlight);
            AudioManager.PlayAt("push", from, 1f, 0.1f, 50f);
            if (window >= 0 && windows != null && window < windows.Length && windows[window] != null)
                Shout(windows[window]);
            string who = NameOf(thrower);
            if (NetworkManager != null && target == NetworkManager.LocalClientId) ShowJudgement($"{who} кинул ботинок!", new Color(0.9f, 0.5f, 0.2f));
        }

        private static readonly string[] Shouts = { "А ну тихо!", "Кыш!", "Ночь на дворе!", "Сейчас как дам!", "Брысь!" };

        private void Shout(Transform window)
        {
            var go = new GameObject("Shout");
            go.transform.position = window.position + Vector3.up * 0.9f;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 64;
            text.characterSize = 0.07f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.95f, 0.7f);
            text.text = Shouts[Random.Range(0, Shouts.Length)];
            var camera = Camera.main;
            if (camera != null) go.transform.rotation = Quaternion.LookRotation(go.transform.position - camera.transform.position);
            Destroy(go, 1.6f);
        }

        private void PlayVoice(ulong clientId, byte lane, int semitone)
        {
            var cat = CatController.FindByOwner(clientId);
            Vector3 at = cat != null ? cat.transform.position + Vector3.up * 0.5f : Vector3.zero;
            if (lane == 0)
            {
                float pitch = Mathf.Pow(2f, (semitone - 5) / 12f);
                AudioManager.PlayAtPitch("meow", at, 0.9f, pitch, 40f);
            }
            else AudioManager.PlayAtPitch("hiss", at, 0.7f, 1.1f, 40f);
            if (cat != null && cat.TryGetComponent(out CatVisual visual)) visual.Pulse();
        }

        // ───────────────────────── Клиент: музыка, ввод, судейство ─────────────────────────

        /// <summary>Время песни по своему звуку (секунды от начала дорожки).</summary>
        public double SongTime
        {
            get
            {
                if (_dspStart > 0) return AudioSettings.dspTime - _dspStart;
                if (SongStart.Value < 0 || NetworkManager == null) return -999;
                return NetworkManager.ServerTime.Time - SongStart.Value;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned) return;

            // Коты стоят на своих местах весь концерт.
            if (Phase == MiniGamePhase.Playing) CatController.ControlsLocked = true;

            ScheduleMusic();
            if (Phase == MiniGamePhase.Playing && SongStart.Value > 0)
            {
                HandleInput();
                CheckMisses();
            }
            UpdateTrackView();
        }

        private void ScheduleMusic()
        {
            if (_dspStart > 0 || SongStart.Value < 0 || music == null || NetworkManager == null) return;
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.clip = music;
            }
            _source.volume = Mathf.Clamp01(GameSettings.MusicVolume) * 0.85f + 0.1f;
            double untilStart = SongStart.Value - NetworkManager.ServerTime.Time;
            if (untilStart > 0.05)
            {
                _dspStart = AudioSettings.dspTime + untilStart;
                _source.PlayScheduled(_dspStart);
            }
            else
            {
                // Опоздали (долго грузились) — догоняем с нужного места.
                float offset = (float)-untilStart;
                if (offset >= music.length) { _dspStart = AudioSettings.dspTime - offset; return; }
                _source.time = offset;
                _source.Play();
                _dspStart = AudioSettings.dspTime - offset;
            }
        }

        private void HandleInput()
        {
            var keyboard = Maoshi.GameKeyboard.Current;
            var mouse = Mouse.current;
            bool lane0 = (keyboard != null && keyboard.eKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame && !CatInput.UIWantsCursor);
            bool lane1 = (keyboard != null && keyboard.qKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame);
            bool throwBoot = keyboard != null && keyboard.rKey.wasPressedThisFrame;

            if (throwBoot && MyBoots > 0) ThrowRpc();
            if (SongTime < _stunUntil) return; // сбит ботинком — не поётся
            if (lane0) Press(0);
            if (lane1) Press(1);
        }

        private void Press(byte lane)
        {
            double t = SongTime;
            int best = -1;
            double bestDelta = Good;
            for (int i = _nextMissCheck; i < _notes.Count; i++)
            {
                double delta = _notes[i].Time - t;
                if (delta > Good) break;
                if (_notes[i].Lane != lane || _myResults.ContainsKey(i)) continue;
                if (Math.Abs(delta) <= bestDelta) { bestDelta = Math.Abs(delta); best = i; }
            }
            if (best < 0)
            {
                // Мимо нот — просто голос, без очков (и без штрафа).
                PlayVoice(LocalId, lane, lane == 0 ? 0 : 0);
                return;
            }

            byte result = bestDelta <= Perfect ? (byte)2 : (byte)1;
            _myResults[best] = result;
            _localCombo++;
            PlayVoice(LocalId, lane, _notes[best].Semitone);
            ResultRpc(best, result);
            ShowJudgement(result == 2 ? "Отлично!" : "Хорошо", result == 2 ? new Color(1f, 0.85f, 0.3f) : new Color(0.75f, 0.9f, 1f));
            if (_noteViews.TryGetValue(best, out var view))
            {
                view.AddToClassList("concert-note--hit");
                view.schedule.Execute(() => { view.RemoveFromHierarchy(); }).StartingIn(180);
                _noteViews.Remove(best);
            }
        }

        private void CheckMisses()
        {
            double t = SongTime;
            while (_nextMissCheck < _notes.Count && _notes[_nextMissCheck].Time + Good < t)
            {
                int i = _nextMissCheck++;
                if (_myResults.ContainsKey(i)) continue;
                _myResults[i] = 0;
                _localCombo = 0;
                ResultRpc(i, 0);
                if (t >= _stunUntil) ShowJudgement("Мимо", new Color(0.7f, 0.7f, 0.75f));
            }
        }

        private ulong LocalId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        private int MyBoots
        {
            get
            {
                foreach (var p in Players) if (p.ClientId == LocalId) return p.Boots;
                return 0;
            }
        }

        private static string NameOf(ulong id)
        {
            var player = NetworkPlayer.FindByClientId(id);
            return player != null ? player.DisplayName : "Кто-то";
        }

        // ───────────────────────── Дорожка нот ─────────────────────────

        public override string StatusText =>
            Phase == MiniGamePhase.Playing
                ? "Пой в такт: E — «мяу», Q — «шшш» (или ЛКМ / ПКМ). Серия 12 нот — ботинок, R — кинуть в лидера."
                : "Ночной концерт под окнами: E — «мяу», Q — «шшш» в такт с нотами.";

        public string ModalKey => Phase == MiniGamePhase.Countdown || Phase == MiniGamePhase.Playing ? "concert" : "";

        public VisualElement BuildModal()
        {
            var root = new VisualElement();
            root.AddToClassList("concert");
            root.pickingMode = PickingMode.Ignore;

            _track = new VisualElement();
            _track.AddToClassList("concert-track");
            _track.pickingMode = PickingMode.Ignore;
            for (int lane = 0; lane < 2; lane++)
            {
                var row = new VisualElement();
                row.AddToClassList("concert-lane");
                row.AddToClassList(lane == 0 ? "concert-lane--meow" : "concert-lane--hiss");
                row.pickingMode = PickingMode.Ignore;
                var key = UIKit.Key(lane == 0 ? "E" : "Q", lane == 0 ? "мяу" : "шшш");
                key.AddToClassList("concert-lane__key");
                row.Add(key);
                _track.Add(row);
            }
            var hitLine = new VisualElement();
            hitLine.AddToClassList("concert-hitline");
            hitLine.pickingMode = PickingMode.Ignore;
            _track.Add(hitLine);
            _notesLayer = new VisualElement();
            _notesLayer.AddToClassList("concert-notes");
            _notesLayer.pickingMode = PickingMode.Ignore;
            _track.Add(_notesLayer);
            var info = new VisualElement();
            info.AddToClassList("concert-info");
            info.pickingMode = PickingMode.Ignore;
            _judgement = UIKit.Text("", "concert-judgement");
            _combo = UIKit.Text("", "concert-combo");
            _boots = UIKit.Text("", "concert-boots");
            info.Add(_judgement);
            info.Add(_combo);
            info.Add(_boots);
            root.Add(info);
            root.Add(_track);

            _noteViews.Clear();
            return root;
        }

        private void ShowJudgement(string text, Color color)
        {
            if (_judgement == null) return;
            _judgement.text = text;
            _judgement.style.color = color;
            _judgementShownAt = Time.unscaledTime;
        }

        private void UpdateTrackView()
        {
            if (_notesLayer == null || _notesLayer.panel == null) return;
            double t = SongTime;
            float width = _notesLayer.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 10f) return;
            const float hitX = 96f;

            // Видимые ноты.
            for (int i = Mathf.Max(0, _nextMissCheck - 2); i < _notes.Count; i++)
            {
                var note = _notes[i];
                double ahead = note.Time - t;
                if (ahead > Lookahead) break;
                if (_myResults.ContainsKey(i)) continue;
                if (!_noteViews.TryGetValue(i, out var view))
                {
                    view = new VisualElement();
                    view.AddToClassList("concert-note");
                    view.AddToClassList(note.Lane == 0 ? "concert-note--meow" : "concert-note--hiss");
                    view.pickingMode = PickingMode.Ignore;
                    var label = new Label(note.Lane == 0 ? "♪" : "!");
                    label.AddToClassList("concert-note__label");
                    label.pickingMode = PickingMode.Ignore;
                    view.Add(label);
                    _notesLayer.Add(view);
                    _noteViews[i] = view;
                }
                float x = hitX + (float)(ahead / Lookahead) * (width - hitX - 20f);
                view.style.left = x - 22f;
                view.style.top = note.Lane == 0 ? 10f : 66f;
            }

            // Убрать пропущенные.
            var stale = new List<int>();
            foreach (var pair in _noteViews) if (_myResults.ContainsKey(pair.Key)) stale.Add(pair.Key);
            foreach (var i in stale)
            {
                var view = _noteViews[i];
                if (_myResults[i] == 0) view.AddToClassList("concert-note--miss");
                view.schedule.Execute(() => view.RemoveFromHierarchy()).StartingIn(200);
                _noteViews.Remove(i);
            }

            bool stunned = t < _stunUntil;
            _track.EnableInClassList("concert-track--stunned", stunned);

            if (_judgement != null)
            {
                float age = Time.unscaledTime - _judgementShownAt;
                _judgement.style.opacity = Mathf.Clamp01(1.4f - age * 1.6f);
                _judgement.style.scale = new Scale(Vector3.one * (1f + Mathf.Max(0f, 0.25f - age) * 1.2f));
            }
            if (_combo != null) _combo.text = _localCombo >= 3 ? $"Серия {_localCombo}" : "";
            if (_boots != null)
            {
                int boots = MyBoots;
                _boots.text = boots > 0 ? $"Ботинок ×{boots} — R, кинуть в лидера" : $"До ботинка: {comboPerBoot - _localCombo % comboPerBoot}";
            }

            if (t < -0.2 && Phase == MiniGamePhase.Playing && _judgement != null && Time.unscaledTime - _judgementShownAt > 0.9f)
                ShowJudgement(t < -1.2 ? "Приготовились…" : "Поём!", new Color(1f, 0.95f, 0.8f));
        }

        public override void OnDestroy()
        {
            CatVoice.Suppressed = false;
            base.OnDestroy();
        }
    }
}
