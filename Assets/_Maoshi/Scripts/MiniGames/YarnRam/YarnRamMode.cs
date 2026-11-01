using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.YarnRam
{
    /// <summary>
    /// «Клубки в корзинку»: на крыше гигантские клубки, у каждого кота по краю крыши своя корзинка его цвета.
    /// Пинай клубки лапой (ЛКМ / F) и закатывай в свою корзинку: обычный клубок — 1 очко, золотой — 3.
    /// Чужие клубки можно выбивать, а быстрым клубком — сбивать соперников с лап. Можно встать в чужую корзинку и загородить её.
    /// Упал с крыши — не выбываешь, но теряешь очко и через пару секунд возвращаешься в центр.
    /// Места — по очкам. Со временем клубков больше, упавшие клубки падают обратно с неба.
    /// </summary>
    public class YarnRamMode : MiniGameMode
    {
        [Header("Крыша")]
        [SerializeField] private float roofY = 8f;
        [SerializeField] private float roofHalfSize = 7f;
        [Tooltip("Насколько ниже крыши кот считается упавшим.")]
        [SerializeField] private float eliminationDepth = 2f;
        [Tooltip("Сколько очков теряет упавший с крыши.")]
        [SerializeField] private int fallPenalty = 1;

        [Header("Клубки")]
        [SerializeField] private YarnBall[] balls;
        [Tooltip("Где ждут клубки, которые ещё не в игре (под крышей, не видно).")]
        [SerializeField] private Transform ballStorage;
        [SerializeField] private int startBalls = 3;
        [Tooltip("Каждые столько секунд с неба падает ещё один клубок.")]
        [SerializeField] private float extraBallInterval = 20f;
        [Tooltip("Упавший с крыши клубок возвращается сверху через столько секунд.")]
        [SerializeField] private float respawnDelay = 1.5f;
        [SerializeField] private float dropHeight = 5f;

        [Header("Корзинки")]
        [SerializeField] private YarnBasket[] baskets;
        [Tooltip("Клубок засчитан, если его центр в корзинке не выше стольких метров над крышей.")]
        [SerializeField] private float basketMaxHeight = 1.3f;

        [Header("Скамейка (старое поле, не используется)")]
        [SerializeField] private Transform bench;

        public readonly NetworkVariable<int> ActiveBalls = new();
        /// <summary>Хозяин каждой корзинки (ulong.MaxValue — корзинка не нужна).</summary>
        public NetworkList<ulong> BasketOwners;

        private readonly Dictionary<YarnBall, double> _fallenAt = new();
        private readonly Dictionary<ulong, double> _respawnUntil = new();
        private float _nextLabelRefresh;

        protected override bool UsesScores => true;

        public override string StatusText => Phase != MiniGamePhase.Playing
            ? "Закатывай клубки в свою корзинку (её цвет — цвет твоего кота)! Пинок — ЛКМ / F. Золотой клубок — 3 очка."
            : $"Пинок — ЛКМ / F. Клубок в своей корзинке — очко, золотой — 3. Упал с крыши — минус очко. Клубков: {ActiveBalls.Value}";

        protected override void Awake()
        {
            BasketOwners = new NetworkList<ulong>();
            base.Awake();
        }

        /// <summary>Все загрузились (сетевые объекты сцены уже заспавнены) — раздаём корзинки и расставляем клубки.</summary>
        protected override void OnPlayersReady()
        {
            BasketOwners.Clear();
            if (baskets != null)
                for (int i = 0; i < baskets.Length; i++)
                    BasketOwners.Add(i < Participants.Count ? Participants[i] : ulong.MaxValue);

            if (balls == null) return;

            for (int i = 0; i < balls.Length; i++)
            {
                if (balls[i] == null) continue;
                if (i < startBalls) Activate(balls[i], StartPosition(i));
                else Park(balls[i], i);
            }

            ActiveBalls.Value = Mathf.Min(startBalls, balls.Length);
        }

        // ───────────────────────── Сервер ─────────────────────────

        protected override void OnGameTick()
        {
            // 1. Упал с крыши — минус очко и обратно в центр (не выбывает).
            for (int i = 0; i < Participants.Count; i++)
            {
                ulong clientId = Participants[i];
                var cat = CatController.FindByOwner(clientId);
                if (cat == null || cat.transform.position.y >= roofY - eliminationDepth) continue;
                if (_respawnUntil.TryGetValue(clientId, out double until) && Now < until) continue;
                if (RescueDuringStartGrace(cat)) continue;

                _respawnUntil[clientId] = Now + 1.5;
                if (Phase == MiniGamePhase.Playing) AddScore(clientId, -fallPenalty);
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, Participants.Count);
                Vector3 spot = new(Mathf.Cos(angle) * 1.6f, roofY + 0.4f, Mathf.Sin(angle) * 1.6f);
                cat.SetRespawnAndTeleportRpc(spot, Quaternion.LookRotation(-new Vector3(spot.x, 0f, spot.z).normalized));
            }

            if (Phase != MiniGamePhase.Playing || balls == null) return;

            // 2. Со временем — больше клубков (последний — золотой).
            int due = Mathf.Min(balls.Length, startBalls + (int)(ElapsedPlaying / extraBallInterval));
            while (ActiveBalls.Value < due)
            {
                var ball = balls[ActiveBalls.Value];
                ActiveBalls.Value++;
                if (ball != null) Activate(ball, DropPosition());
            }

            for (int i = 0; i < ActiveBalls.Value && i < balls.Length; i++)
            {
                var ball = balls[i];
                if (ball == null) continue;

                // 3. Клубок в корзинке — очки хозяину, клубок падает обратно с неба.
                int basket = BasketAt(ball.transform.position);
                if (basket >= 0)
                {
                    ulong owner = BasketOwners[basket];
                    AddScore(owner, ball.Points);
                    ScoredRpc(basket, ball.Points, owner);
                    ball.ResetTo(DropPosition());
                    continue;
                }

                // 4. Упавшие клубки возвращаются сверху.
                if (ball.transform.position.y < roofY - 3f)
                {
                    if (!_fallenAt.TryGetValue(ball, out double since)) _fallenAt[ball] = Now;
                    else if (Now - since > respawnDelay)
                    {
                        _fallenAt.Remove(ball);
                        ball.ResetTo(DropPosition());
                    }
                }
            }
        }

        private int BasketAt(Vector3 position)
        {
            if (baskets == null) return -1;
            for (int i = 0; i < baskets.Length && i < BasketOwners.Count; i++)
            {
                if (baskets[i] == null || BasketOwners[i] == ulong.MaxValue) continue;
                if (baskets[i].Contains(position, basketMaxHeight)) return i;
            }
            return -1;
        }

        [Rpc(SendTo.Everyone)]
        private void ScoredRpc(int basket, int points, ulong owner)
        {
            if (baskets == null || basket < 0 || basket >= baskets.Length || baskets[basket] == null) return;
            baskets[basket].Flash();
            Vector3 at = baskets[basket].transform.position;
            AudioManager.PlayAt(points > 1 ? "fish_big" : "fish", at, 1f, 0.05f, 40f);
            if (NetworkManager.Singleton != null && owner == NetworkManager.Singleton.LocalClientId) AudioManager.Play("ui_confirm", 0.6f);
        }

        private void Activate(YarnBall ball, Vector3 position)
        {
            ball.Body.isKinematic = false;
            ball.ResetTo(position);
        }

        private void Park(YarnBall ball, int index)
        {
            ball.Body.isKinematic = true;
            Vector3 storage = ballStorage != null ? ballStorage.position : new Vector3(0f, roofY - 6f, 0f);
            ball.ResetTo(storage + Vector3.right * (index * 2f));
        }

        /// <summary>Стартовые клубки — треугольником вокруг центра крыши.</summary>
        private Vector3 StartPosition(int index)
        {
            float angle = index * Mathf.PI * 2f / Mathf.Max(1, startBalls) + 0.5f;
            return new Vector3(Mathf.Cos(angle) * 2f, roofY + 1f, Mathf.Sin(angle) * 2f);
        }

        /// <summary>Случайная точка над серединой крыши (не над корзинками у края).</summary>
        private Vector3 DropPosition()
        {
            float limit = roofHalfSize - 3.2f;
            return new Vector3(Random.Range(-limit, limit), roofY + dropHeight, Random.Range(-limit, limit));
        }

        // ───────────────────────── Клиенты ─────────────────────────

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned || baskets == null || Time.time < _nextLabelRefresh) return;
            _nextLabelRefresh = Time.time + 0.25f;
            for (int i = 0; i < baskets.Length; i++)
            {
                if (baskets[i] == null) continue;
                ulong owner = i < BasketOwners.Count ? BasketOwners[i] : ulong.MaxValue;
                var player = owner != ulong.MaxValue ? NetworkPlayer.FindByClientId(owner) : null;
                baskets[i].Show(player != null);
                if (player == null) continue;
                bool me = NetworkManager.Singleton != null && owner == NetworkManager.Singleton.LocalClientId;
                baskets[i].SetOwner(player.Color, $"{(me ? "ТВОЯ\n" : "")}{player.DisplayName}: {GetScore(owner)}");
            }
        }
    }
}
