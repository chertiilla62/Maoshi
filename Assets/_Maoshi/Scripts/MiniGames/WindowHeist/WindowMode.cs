using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Gameplay;
using Maoshi.Session;
using Maoshi.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.MiniGames.WindowHeist
{
    /// <summary>
    /// «Форточка»: коты пробираются через окно в квартиру, где спит пёс, и тащат добычу на балкон.
    /// - F у добычи — взять в зубы (одна вещь). Дотащил до балкона — очки по ценности.
    /// - Шум (общий, 0..100): бег внутри шумит, рядом с псом — вдвое; крадучись (C) — тихо,
    ///   но тяжёлая добыча шумит даже так. Драки (кот сбит с лап) и уроненная добыча — всплески. Шум сам спадает.
    /// - 70+ — пёс ворочается и рычит. 100 — просыпается и гонится за котами (сначала за тем, кто с добычей).
    ///   Поймал — добыча падает на пол, кот вылетает на балкон. Через 8 с (или после поимки) пёс идёт на лежанку.
    /// - Сбитый с лап кот (два толчка) роняет добычу — её может подобрать кто угодно.
    /// Всё решает сервер: добыча, шум, пёс, очки.
    /// </summary>
    public class WindowMode : MiniGameMode, IMiniGameModal
    {
        [Header("Форточка")]
        [SerializeField] private Loot[] loot;
        [SerializeField] private Dog dog;
        [Tooltip("Куча добычи на балконе — сюда складывается принесённое.")]
        [SerializeField] private Transform stash;
        [Tooltip("Граница квартиры: внутри — z больше этого значения (стена с окном).")]
        [SerializeField] private float wallZ = -6f;
        [SerializeField] private Vector2 insideX = new(-8f, 8f);

        [Header("Шум")]
        [SerializeField] private float runNoise = 24f;
        [SerializeField] private float heavyNoise = 11f;
        [SerializeField] private float nearDogRadius = 3.2f;
        [SerializeField] private float decay = 13f;
        [SerializeField] private float knockNoise = 16f;
        [SerializeField] private float dropNoise = 14f;
        [SerializeField] private float quietSpeed = 2.6f;

        [Header("Пёс")]
        [SerializeField] private float chaseSeconds = 8f;
        [SerializeField] private float catchRadius = 1.1f;
        [SerializeField] private float noiseAfterChase = 35f;
        [Tooltip("Сколько добычи (доля очков) отбирает пёс у пойманного кота.")]
        [SerializeField, Range(0f, 1f)] private float caughtPenalty = 0.3f;

        public readonly NetworkVariable<float> Noise = new();

        // Сервер.
        private readonly Dictionary<ulong, Vector3> _lastPosition = new();
        private readonly Dictionary<ulong, float> _speed = new();
        private readonly Dictionary<ulong, CatState> _lastCatState = new();
        private double _stateUntil;
        private int _delivered;

        // Интерфейс.
        private VisualElement _meterFill;
        private Label _meterLabel;

        protected override bool UsesScores => true;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (loot != null) foreach (var item in loot) if (item != null) item.Mode = this;
        }

        public bool IsInside(Vector3 p) => p.z > wallZ && p.x > insideX.x && p.x < insideX.y;

        public Loot CarriedBy(ulong clientId)
        {
            if (loot == null) return null;
            foreach (var item in loot)
                if (item != null && item.State.Value == Loot.LootState.Carried && item.Carrier.Value == clientId) return item;
            return null;
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnPlayersReady()
        {
            Noise.Value = 0f;
            _delivered = 0;
        }

        protected override void OnGameTick()
        {
            float dt = Time.deltaTime;
            TrackCats(dt);
            UpdateCarriers();
            TickDog();

            if (dog != null) dog.Restless = Noise.Value / 100f;
            if (loot != null && _delivered >= loot.Length) FinishGame();
        }

        private void TrackCats(float dt)
        {
            if (dt <= 0f) return;
            float noise = Noise.Value;
            bool anyNoise = false;
            foreach (var id in Participants)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                Vector3 position = cat.transform.position;
                if (_lastPosition.TryGetValue(id, out var last))
                {
                    float step = (position - last).magnitude;
                    float frame = step > 1.5f ? 0f : Mathf.Min(step / dt, 30f);
                    float s = _speed.TryGetValue(id, out var v) ? v : 0f;
                    _speed[id] = Mathf.Lerp(s, frame, 1f - Mathf.Exp(-10f * dt));
                }
                _lastPosition[id] = position;

                // Драка: кот только что сбит с лап — грохот.
                var state = cat.State.Value;
                if (_lastCatState.TryGetValue(id, out var previous) && previous != CatState.Knocked && state == CatState.Knocked && IsInside(position))
                    noise += knockNoise;
                _lastCatState[id] = state;

                if (Dogs.Awake(dog) || !IsInside(position)) continue;
                float speed = _speed.TryGetValue(id, out var sp) ? sp : 0f;
                var carried = CarriedBy(id);
                float add = 0f;
                if (speed > quietSpeed && !cat.Crouching.Value) add += runNoise * (carried != null && carried.Weight >= 1 ? 1.4f : 1f);
                if (carried != null && carried.Weight >= 2 && speed > 0.6f) add += heavyNoise;
                if (add > 0f && dog != null && (position - dog.transform.position).sqrMagnitude < nearDogRadius * nearDogRadius) add *= 2f;
                if (add > 0f) anyNoise = true;
                noise += add * dt;
            }
            if (!anyNoise && !Dogs.Awake(dog)) noise -= decay * dt;
            Noise.Value = Mathf.Clamp(noise, 0f, 100f);
        }

        private void UpdateCarriers()
        {
            if (loot == null) return;
            foreach (var item in loot)
            {
                if (item == null || item.State.Value != Loot.LootState.Carried) continue;
                var cat = CatController.FindByOwner(item.Carrier.Value);
                if (cat == null || cat.State.Value == CatState.Knocked)
                {
                    DropAt(item, cat != null ? cat.transform.position : item.Position.Value);
                    continue;
                }
                // Вынес на балкон — в кучу, очки хозяину.
                if (cat.transform.position.z < wallZ - 0.4f)
                {
                    ulong owner = item.Carrier.Value;
                    AddScore(owner, item.Value);
                    _delivered++;
                    Vector3 pile = stash != null ? stash.position : cat.transform.position;
                    pile += new Vector3((_delivered % 4 - 1.5f) * 0.3f, 0.05f + (_delivered / 4) * 0.12f, (_delivered % 3 - 1) * 0.2f);
                    item.ServerDeliver(pile);
                    DeliveredRpc(owner, item.Value, cat.transform.position);
                    Debug.Log($"[Maoshi] Форточка: кот {owner} вынес «{item.Title}» (+{item.Value}).");
                }
            }
        }

        private void DropAt(Loot item, Vector3 at)
        {
            at.y = Mathf.Max(0.02f, at.y - 0.25f);
            if (!IsInside(at)) at.z = Mathf.Max(at.z, wallZ + 0.6f);
            item.ServerDrop(at);
            if (IsInside(at)) Noise.Value = Mathf.Min(100f, Noise.Value + dropNoise);
            DropRpc(at);
        }

        /// <summary>Сервер: кот толкнул добычу лапой — взять, если зубы свободны.</summary>
        public void TryPick(Loot item, ulong clientId)
        {
            if (!IsServer || Phase != MiniGamePhase.Playing || item.State.Value != Loot.LootState.Free) return;
            if (CarriedBy(clientId) != null || !Participants.Contains(clientId)) return;
            var cat = CatController.FindByOwner(clientId);
            if (cat == null || cat.State.Value != CatState.Normal) return;
            item.ServerCarry(clientId);
            Debug.Log($"[Maoshi] Форточка: кот {clientId} взял «{item.Title}».");
            PickRpc(clientId, item.transform.position, item.Weight);
        }

        // ───────────────────────── Пёс ─────────────────────────

        private static class Dogs
        {
            public static bool Awake(Dog d) => d != null && d.State.Value != Dog.DogState.Sleeping;
        }

        private void TickDog()
        {
            if (dog == null) return;
            switch (dog.State.Value)
            {
                case Dog.DogState.Sleeping:
                    if (Noise.Value >= 100f)
                    {
                        dog.ServerWake();
                        Debug.Log("[Maoshi] Форточка: пёс проснулся.");
                        _stateUntil = Now + 1.1;
                    }
                    break;

                case Dog.DogState.Waking:
                    if (Now >= _stateUntil)
                    {
                        _stateUntil = Now + chaseSeconds;
                        var target = PickTarget();
                        if (target != null) dog.ServerChase(target.transform.position);
                        else ReturnDog();
                    }
                    break;

                case Dog.DogState.Chasing:
                {
                    var target = PickTarget();
                    if (target == null || Now >= _stateUntil) { ReturnDog(); break; }
                    dog.ServerChase(target.transform.position);
                    Vector3 offset = target.transform.position - dog.transform.position;
                    offset.y = 0f;
                    if (offset.magnitude <= catchRadius) Catch(target);
                    break;
                }

                case Dog.DogState.Returning:
                    if (dog.ServerAtBed)
                    {
                        dog.ServerSleep();
                        Noise.Value = noiseAfterChase;
                    }
                    break;
            }
        }

        private void ReturnDog()
        {
            dog.ServerReturn();
        }

        /// <summary>Цель пса: ближайший кот в квартире, кот с добычей — в приоритете.</summary>
        private CatController PickTarget()
        {
            CatController best = null;
            float bestScore = float.MaxValue;
            foreach (var id in Participants)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null || !IsInside(cat.transform.position)) continue;
                float d = (cat.transform.position - dog.transform.position).magnitude;
                if (CarriedBy(id) != null) d *= 0.6f;
                if (d < bestScore) { bestScore = d; best = cat; }
            }
            return best;
        }

        private void Catch(CatController cat)
        {
            ulong id = cat.OwnerClientId;
            var carried = CarriedBy(id);
            if (carried != null) DropAt(carried, cat.transform.position);
            dog.BarkRpc();
            // Пойманный теряет 30% уже вынесенной добычи.
            int lost = Mathf.CeilToInt(GetScore(id) * caughtPenalty);
            if (lost > 0) AddScore(id, -lost);
            Debug.Log($"[Maoshi] Форточка: пёс поймал кота {id}, минус {lost}.");
            CaughtRpc(id, cat.transform.position, lost);
            var spawner = CatSpawner.Current;
            if (spawner != null) spawner.ReturnToSpawn(cat);
            dog.ServerStop();
            ReturnDog();
        }

        // ───────────────────────── Сеть: звуки ─────────────────────────

        [Rpc(SendTo.Everyone)]
        private void PickRpc(ulong clientId, Vector3 at, int weight) => AudioManager.PlayAt(weight >= 2 ? "clink" : "chomp", at, 0.8f);

        [Rpc(SendTo.Everyone)]
        private void DeliveredRpc(ulong clientId, int value, Vector3 at) => AudioManager.PlayAt(value >= 3 ? "fish_big" : "fish", at, 0.9f, 0.02f, 40f);

        [Rpc(SendTo.Everyone)]
        private void DropRpc(Vector3 at) => AudioManager.PlayAt("crash", at, 0.7f, 0.1f, 30f);

        [Rpc(SendTo.Everyone)]
        private void CaughtRpc(ulong clientId, Vector3 at, int lost)
        {
            AudioManager.PlayAt("hit", at, 1f, 0.05f, 40f);
            if (NetworkManager == null || clientId != NetworkManager.LocalClientId) return;
            AudioManager.Play("ouch", 0.8f);
            _caughtText = lost > 0 ? $"Пёс поймал тебя и отобрал {lost} (30% добычи)!" : "Пёс поймал тебя! Добыча упала на пол.";
            _caughtUntil = Time.time + 3.5f;
        }

        private string _caughtText = "";
        private float _caughtUntil = -1f;

        // ───────────────────────── Клиент ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned) return;

            // Свой кот с тяжёлой ношей идёт медленнее.
            var local = NetworkManager != null ? CatController.FindByOwner(NetworkManager.LocalClientId) : null;
            if (local != null)
            {
                var carried = CarriedBy(local.OwnerClientId);
                local.SpeedMultiplier = carried == null ? 1f : carried.Weight >= 2 ? 0.68f : carried.Weight == 1 ? 0.86f : 1f;
            }
            if (!IsServer && dog != null) dog.Restless = Noise.Value / 100f;
            UpdateMeter();
        }

        public override void OnNetworkDespawn()
        {
            var local = NetworkManager != null ? CatController.FindByOwner(NetworkManager.LocalClientId) : null;
            if (local != null) local.SpeedMultiplier = 1f;
            base.OnNetworkDespawn();
        }

        private ulong LocalId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Пролезь в окно, стащи добычу и вынеси на балкон. Не разбуди пса!";
                if (Time.time < _caughtUntil) return _caughtText;
                if (dog != null && dog.State.Value != Dog.DogState.Sleeping)
                    return dog.State.Value == Dog.DogState.Returning ? "Пёс идёт на лежанку…" : "Пёс проснулся! Беги на балкон!";
                var carried = CarriedBy(LocalId);
                if (carried != null)
                    return carried.Weight >= 2 ? $"{carried.Title} (+{carried.Value}) — тяжело и шумно, неси на балкон!" : $"{carried.Title} (+{carried.Value}) — неси на балкон!";
                return "F — взять добычу. Крадись (C): бег шумит, рядом с псом — вдвое.";
            }
        }

        public string ModalKey => Phase == MiniGamePhase.Playing || Phase == MiniGamePhase.Countdown ? "window-noise" : "";

        public VisualElement BuildModal()
        {
            var root = new VisualElement();
            root.AddToClassList("noise-meter");
            root.pickingMode = PickingMode.Ignore;
            _meterLabel = UIKit.Text("Пёс спит", "noise-meter__label");
            root.Add(_meterLabel);
            var bar = new VisualElement();
            bar.AddToClassList("noise-meter__bar");
            bar.pickingMode = PickingMode.Ignore;
            _meterFill = new VisualElement();
            _meterFill.AddToClassList("noise-meter__fill");
            _meterFill.pickingMode = PickingMode.Ignore;
            bar.Add(_meterFill);
            var mark = new VisualElement();
            mark.AddToClassList("noise-meter__mark");
            mark.pickingMode = PickingMode.Ignore;
            bar.Add(mark);
            root.Add(bar);
            return root;
        }

        private void UpdateMeter()
        {
            if (_meterFill == null || _meterFill.panel == null) return;
            float n = Noise.Value / 100f;
            _meterFill.style.width = Length.Percent(n * 100f);
            _meterFill.style.backgroundColor = Color.Lerp(new Color(0.45f, 0.8f, 0.5f), new Color(0.92f, 0.3f, 0.25f), n);
            string text;
            if (dog != null && dog.State.Value != Dog.DogState.Sleeping) text = dog.State.Value == Dog.DogState.Returning ? "Пёс укладывается…" : "ПЁС ПРОСНУЛСЯ!";
            else if (n >= 0.7f) text = "Пёс ворочается… тише!";
            else if (n >= 0.35f) text = "Пёс сопит";
            else text = "Пёс крепко спит";
            if (_meterLabel.text != text) _meterLabel.text = text;
        }
    }
}
