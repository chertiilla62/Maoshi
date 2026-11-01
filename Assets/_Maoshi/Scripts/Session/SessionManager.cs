using System.Collections;
using System.Collections.Generic;
using Maoshi.Network;
using Maoshi.Showdown;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.Session
{
    /// <summary>
    /// Дирижёр партии: двор (голосование) → мини-игра → итоги → двор (аукцион за зону) → … → финал.
    /// Создаётся сервером при старте игры и живёт между сценами (DontDestroyOnLoad).
    /// Хранит зоны, раунды и аукцион; рыба лежит у NetworkPlayer.
    /// Всё, что меняет счёт, рыбу и зоны, делает только сервер.
    /// </summary>
    public class SessionManager : NetworkBehaviour
    {
        /// <summary>Значение Owner для «никто» в итогах аукциона.</summary>
        public const ulong NoOne = ulong.MaxValue;

        public static SessionManager Instance { get; private set; }

        [Header("Партия")]
        [SerializeField] private MiniGameCatalog catalog;
        [SerializeField] private string hubScene = "Hub";
        [Tooltip("Сколько мини-игр в партии.")]
        [SerializeField] private int roundsPerSession = 6;
        [SerializeField] private float resultsDuration = 6f;

        [Header("Аукцион")]
        [SerializeField] private float biddingDuration = 20f;
        [Tooltip("Если все уже поставили — до конца не больше стольких секунд.")]
        [SerializeField] private float allBidRemaining = 1.5f;
        [SerializeField] private float revealDuration = 4f;
        [Tooltip("Владелец защищает свою зону: к его ставке прибавляется бонус.")]
        [SerializeField] private int defenderBonus = 2;

        [Header("Перекуп зоны")]
        [Tooltip("Перекупить чужую зону можно за её цену + столько (0.3 = +30%).")]
        [SerializeField] private float buyoutMarkup = 0.3f;
        [Tooltip("Дешевле этого зону не перекупить (даже если её взяли даром).")]
        [SerializeField] private int minBuyoutPrice = 3;
        [Tooltip("Подойти к флажку зоны ближе стольких метров (по горизонтали).")]
        [SerializeField] private float buyoutRadius = 5.5f;
        [Tooltip("Сколько секунд держать E, чтобы перекупить.")]
        [SerializeField] private float buyoutHold = 1f;

        // ── Общее состояние партии ──
        public readonly NetworkVariable<SessionPhase> Phase = new(SessionPhase.Hub);
        public readonly NetworkVariable<int> RoundNumber = new();
        public readonly NetworkVariable<int> TotalRounds = new(6);
        public readonly NetworkVariable<FixedString64Bytes> CurrentMiniGameId = new();
        public NetworkList<ZoneState> Zones;

        // ── Аукцион ──
        public readonly NetworkVariable<AuctionStage> Auction = new(AuctionStage.None);
        public readonly NetworkVariable<int> AuctionZone = new(-1);
        public readonly NetworkVariable<double> AuctionEndTime = new();
        public readonly NetworkVariable<ulong> AuctionWinner = new(NoOne);
        public readonly NetworkVariable<int> AuctionPrice = new();
        /// <summary>Кто уже сделал ставку (сумма скрыта до вскрытия).</summary>
        public NetworkList<ulong> BidPlaced;
        /// <summary>Открытые ставки после вскрытия: ClientId + Score (= ставка).</summary>
        public NetworkList<PlayerScore> RevealedBids;

        /// <summary>Новость двора («Вася перекупил Помойку…») и когда её показали.</summary>
        public readonly NetworkVariable<FixedString128Bytes> ZoneNews = new();
        public readonly NetworkVariable<double> ZoneNewsTime = new(-100);

        /// <summary>Номинации партии (заполняются при переходе в финал).</summary>
        public NetworkList<Nomination> Nominations;
        /// <summary>Номер финала (случайный): по нему клиенты начисляют банки ровно один раз.</summary>
        public readonly NetworkVariable<int> FinalId = new();

        public MiniGameCatalog Catalog => catalog;
        public int DefenderBonus => defenderBonus;

        /// <summary>Сервер: ID последней сыгранной мини-игры (чтобы не предлагать её сразу снова).</summary>
        public string LastMiniGameId { get; private set; } = "";

        /// <summary>Сервер: сыгранные мини-игры, последние в конце (голосование не предлагает самые свежие).</summary>
        public IReadOnlyList<string> PlayedHistory => _playedHistory;
        private readonly List<string> _playedHistory = new();

        /// <summary>Последние итоги мини-игры (у всех игроков) и до какого момента их показывать.</summary>
        public IReadOnlyList<ResultEntry> LastResults => _lastResults;
        public string LastResultsTitle { get; private set; } = "";
        public float ResultsVisibleUntil { get; private set; }
        public bool ResultsVisible => Time.unscaledTime < ResultsVisibleUntil;

        public float AuctionTimeLeft => Mathf.Max(0f, (float)(AuctionEndTime.Value - Now));

        private readonly List<ResultEntry> _lastResults = new();
        private readonly Dictionary<ulong, int> _bids = new();
        private double Now => NetworkManager.ServerTime.Time;

        private void Awake()
        {
            Zones = new NetworkList<ZoneState>();
            BidPlaced = new NetworkList<ulong>();
            RevealedBids = new NetworkList<PlayerScore>();
            Nominations = new NetworkList<Nomination>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!IsServer) return;
            TotalRounds.Value = Mathf.Max(1, roundsPerSession);
            SessionStats.Reset();
            CreateZones();
            NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        // ───────────────────────── Зоны ─────────────────────────

        private void CreateZones()
        {
            Zones.Clear();
            int count = YardZones.CountFor(NetworkPlayer.All.Count);
            for (int i = 0; i < count; i++)
                Zones.Add(new ZoneState { ZoneId = (byte)i, HasOwner = false, Owner = 0, Price = 0, LockedRound = -1 });
        }

        /// <summary>Сколько зон у игрока.</summary>
        public int ZoneCount(ulong clientId)
        {
            int count = 0;
            foreach (var zone in Zones)
            {
                if (zone.HasOwner && zone.Owner == clientId) count++;
            }

            return count;
        }

        private int ZoneIndexById(int zoneId)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                if (Zones[i].ZoneId == zoneId) return i;
            }

            return -1;
        }

        /// <summary>Владелец зоны, если есть.</summary>
        public bool TryGetZoneOwner(int zoneId, out ulong owner)
        {
            owner = 0;
            int index = ZoneIndexById(zoneId);
            if (index < 0 || !Zones[index].HasOwner) return false;
            owner = Zones[index].Owner;
            return true;
        }

        // ───────────────────────── Мини-игры ─────────────────────────

        /// <summary>Можно ли сейчас запускать мини-игру (только сервер и только из двора во время голосования).</summary>
        public bool CanStartMiniGame => IsServer && Phase.Value == SessionPhase.Hub;

        /// <summary>Сервер: запустить мини-игру по ID.</summary>
        public void StartMiniGame(string miniGameId)
        {
            if (!CanStartMiniGame) return;

            var definition = catalog != null ? catalog.Find(miniGameId) : null;
            if (definition == null)
            {
                Debug.LogError($"[Maoshi] Мини-игра '{miniGameId}' не найдена в каталоге.");
                return;
            }

            RoundNumber.Value++;
            LastMiniGameId = definition.Id;
            _playedHistory.Add(definition.Id);
            CurrentMiniGameId.Value = new FixedString64Bytes(definition.Id);
            Phase.Value = SessionPhase.MiniGame;

            NetworkManager.SceneManager.LoadScene(definition.SceneName, LoadSceneMode.Single);
        }

        /// <summary>Сервер: мини-игра закончилась — начисляем рыбу и показываем итоги.</summary>
        public void ReportResults(string miniGameId, List<Placement> placements)
        {
            if (!IsServer || Phase.Value != SessionPhase.MiniGame) return;

            var definition = catalog != null ? catalog.Find(miniGameId) : null;
            string title = definition != null ? definition.DisplayName : miniGameId;

            var entries = new List<ResultEntry>();
            foreach (var placement in placements)
            {
                var player = NetworkPlayer.FindByClientId(placement.ClientId);
                if (player == null) continue; // игрок ушёл

                int fish = definition != null ? definition.FishForPlace(placement.Place) : 0;
                player.AddFish(fish);
                SessionStats.Add(placement.ClientId, Stat.FishEarned, fish);
                if (placement.Place == 1) SessionStats.Add(placement.ClientId, Stat.Wins);
                if (placements.Count > 1 && placement.Place == MaxPlace(placements)) SessionStats.Add(placement.ClientId, Stat.LastPlaces);

                entries.Add(new ResultEntry
                {
                    ClientId = placement.ClientId,
                    Place = placement.Place,
                    FishAwarded = fish,
                    TotalFish = player.Fish.Value
                });
            }

            ResultsUtil.SortByPlace(entries);

            Phase.Value = SessionPhase.Results;
            ShowResultsRpc(title, entries.ToArray());
            StartCoroutine(ReturnToHubAfterDelay());
        }

        private static int MaxPlace(List<Placement> placements)
        {
            int max = 0;
            foreach (var p in placements) max = Mathf.Max(max, p.Place);
            return max;
        }

        [Rpc(SendTo.Everyone)]
        private void ShowResultsRpc(string title, ResultEntry[] entries)
        {
            LastResultsTitle = title;
            _lastResults.Clear();
            _lastResults.AddRange(entries);
            ResultsVisibleUntil = Time.unscaledTime + resultsDuration;
        }

        private IEnumerator ReturnToHubAfterDelay()
        {
            yield return new WaitForSecondsRealtime(resultsDuration);

            // Во дворе сначала аукцион за зону, потом голосование (или финал).
            Phase.Value = SessionPhase.Auction;
            CurrentMiniGameId.Value = default;
            NetworkManager.SceneManager.LoadScene(hubScene, LoadSceneMode.Single);
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != hubScene) return;
            if (Phase.Value == SessionPhase.Auction) StartAuction();
        }

        // ───────────────────────── Аукцион ─────────────────────────

        private void StartAuction()
        {
            int zoneId = PickAuctionZone();
            if (zoneId < 0)
            {
                FinishAuctionPhase();
                return;
            }

            _bids.Clear();
            BidPlaced.Clear();
            RevealedBids.Clear();
            AuctionWinner.Value = NoOne;
            AuctionPrice.Value = 0;
            AuctionZone.Value = zoneId;
            AuctionEndTime.Value = Now + biddingDuration;
            Auction.Value = AuctionStage.Bidding;
        }

        /// <summary>Сначала разыгрываем свободные зоны, когда они кончатся — чужие.</summary>
        private int PickAuctionZone()
        {
            var free = new List<int>();
            var owned = new List<int>();
            foreach (var zone in Zones)
            {
                if (zone.HasOwner) owned.Add(zone.ZoneId);
                else free.Add(zone.ZoneId);
            }

            var pool = free.Count > 0 ? free : owned;
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : -1;
        }

        /// <summary>Клиент: поставить рыбу (можно менять до конца торгов). Сумма видна только серверу.</summary>
        public void SubmitBid(int amount) => SubmitBidRpc(amount);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitBidRpc(int amount, RpcParams rpcParams = default)
        {
            if (Auction.Value != AuctionStage.Bidding) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            var player = NetworkPlayer.FindByClientId(sender);
            if (player == null) return;

            // Нельзя поставить больше, чем есть.
            _bids[sender] = Mathf.Clamp(amount, 0, player.Fish.Value);
            if (!BidPlaced.Contains(sender)) BidPlaced.Add(sender);
        }

        private void Update()
        {
            if (IsSpawned) LocalBuyoutInput();
            if (!IsSpawned || !IsServer) return;

            // Только в редакторе: F8 во дворе — сразу финал с номинациями (проверить экран без целой партии).
            if (Application.isEditor && Phase.Value == SessionPhase.Hub && Maoshi.GameKeyboard.Current != null &&
                Maoshi.GameKeyboard.Current.f8Key.wasPressedThisFrame)
            {
                DebugFinal();
                return;
            }

            if (Auction.Value == AuctionStage.Bidding)
            {
                int players = NetworkPlayer.All.Count;
                if (players > 0 && BidPlaced.Count >= players && AuctionEndTime.Value - Now > allBidRemaining)
                    AuctionEndTime.Value = Now + allBidRemaining;

                if (Now >= AuctionEndTime.Value) RevealAuction();
            }
            else if (Auction.Value == AuctionStage.Reveal && Now >= AuctionEndTime.Value)
            {
                Auction.Value = AuctionStage.None;
                AuctionZone.Value = -1;
                FinishAuctionPhase();
            }
        }

        private void RevealAuction()
        {
            int zoneId = AuctionZone.Value;
            bool hasOwner = TryGetZoneOwner(zoneId, out ulong owner);

            // Открываем ставки всем.
            RevealedBids.Clear();
            foreach (var pair in _bids)
            {
                if (NetworkPlayer.FindByClientId(pair.Key) != null)
                    RevealedBids.Add(new PlayerScore { ClientId = pair.Key, Score = pair.Value });
            }

            // Владелец участвует всегда (даже без ставки) и получает бонус защиты.
            var effective = new Dictionary<ulong, int>();
            foreach (var pair in _bids)
            {
                if (pair.Value > 0 || (hasOwner && pair.Key == owner)) effective[pair.Key] = pair.Value;
            }

            if (hasOwner)
            {
                effective.TryGetValue(owner, out int ownerBid);
                effective[owner] = ownerBid + defenderBonus;
            }

            int best = int.MinValue;
            foreach (var pair in effective) best = Mathf.Max(best, pair.Value);

            // В «Кошачью свалку» попадают все, чья ставка отстаёт от лучшей не больше чем на BidWindow рыб.
            var contenders = new List<ulong>();
            foreach (var pair in effective)
                if (pair.Value >= best - BrawlRules.BidWindow) contenders.Add(pair.Key);

            if (contenders.Count >= 2)
            {
                var showdown = GetComponent<ShowdownManager>();
                if (showdown != null)
                {
                    Auction.Value = AuctionStage.Showdown;
                    showdown.Begin(contenders, zoneId, effective, ApplyAuctionResult);
                    return;
                }

                // Свалки нет (старый префаб) — побеждает лучшая ставка.
                contenders.RemoveAll(id => effective[id] != best);
            }

            ApplyAuctionResult(contenders.Count > 0 ? contenders[Random.Range(0, contenders.Count)] : NoOne);
        }

        /// <summary>Сервер: победитель аукциона (или разборки) получает зону и платит свою ставку.</summary>
        private void ApplyAuctionResult(ulong winner)
        {
            int zoneId = AuctionZone.Value;
            int price = 0;

            // Победитель мог уйти из игры, пока шла разборка.
            if (winner != NoOne && NetworkPlayer.FindByClientId(winner) == null) winner = NoOne;

            if (winner != NoOne)
            {
                _bids.TryGetValue(winner, out price);
                var winnerPlayer = NetworkPlayer.FindByClientId(winner);
                if (winnerPlayer != null) winnerPlayer.AddFish(-price);
                SessionStats.Add(winner, Stat.AuctionSpent, price);

                int index = ZoneIndexById(zoneId);
                if (index >= 0) Zones[index] = new ZoneState { ZoneId = (byte)zoneId, HasOwner = true, Owner = winner, Price = price, LockedRound = RoundNumber.Value };
            }

            AuctionWinner.Value = winner;
            AuctionPrice.Value = price;
            AuctionEndTime.Value = Now + revealDuration;
            Auction.Value = AuctionStage.Reveal;
        }

        private void FinishAuctionPhase()
        {
            // После последнего раунда — финал (с номинациями), иначе снова голосование во дворе.
            if (RoundNumber.Value >= TotalRounds.Value) BuildNominations();
            Phase.Value = RoundNumber.Value >= TotalRounds.Value ? SessionPhase.Final : SessionPhase.Hub;
        }

        // ───────────────────────── Перекуп зоны ─────────────────────────

        /// <summary>Сколько стоит перекупить зону: цена + наценка, не меньше минимума.</summary>
        public int BuyoutPrice(ZoneState zone) => Mathf.Max(minBuyoutPrice, Mathf.CeilToInt(zone.Price * (1f + buyoutMarkup)));

        public bool TryGetZone(int zoneId, out ZoneState zone)
        {
            int index = ZoneIndexById(zoneId);
            zone = index >= 0 ? Zones[index] : default;
            return index >= 0;
        }

        /// <summary>Можно ли сейчас перекупить зону (без проверки расстояния). reason — почему нельзя.</summary>
        public bool CanBuyout(int zoneId, ulong buyer, out string reason)
        {
            reason = "";
            if (!TryGetZone(zoneId, out var zone) || !zone.HasOwner) { reason = "Зона свободна — её разыграют на аукционе"; return false; }
            if (zone.Owner == buyer) { reason = "Это твоя зона"; return false; }
            var voting = Hub.HubVoting.Current;
            var duel = GetComponent<ShowdownManager>();
            if (Phase.Value != SessionPhase.Hub || voting == null || voting.Phase.Value != Hub.VotePhase.Voting || (duel != null && duel.IsActive))
            { reason = "Перекупить можно, пока выбирают игру"; return false; }
            if (zone.LockedRound == RoundNumber.Value) { reason = "Зону только что купили — перекупить можно со следующего раунда"; return false; }
            var player = NetworkPlayer.FindByClientId(buyer);
            int price = BuyoutPrice(zone);
            if (player == null || player.Fish.Value < price) { reason = $"Не хватает рыбы: нужно {price}"; return false; }
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void BuyoutRpc(int zoneId, RpcParams rpcParams = default)
        {
            ulong buyer = rpcParams.Receive.SenderClientId;
            if (!CanBuyout(zoneId, buyer, out _)) return;
            var marker = Hub.ZoneMarker.Find(zoneId);
            var cat = Cat.CatController.FindByOwner(buyer);
            if (marker == null || cat == null || marker.HorizontalDistance(cat.transform.position) > buyoutRadius + 1f) return;

            TryGetZone(zoneId, out var zone);
            int price = BuyoutPrice(zone);
            var player = NetworkPlayer.FindByClientId(buyer);
            var previous = NetworkPlayer.FindByClientId(zone.Owner);
            player.AddFish(-price);
            SessionStats.Add(buyer, Stat.Buyouts);
            // Прежний хозяин получает назад то, что заплатил; наценка сгорает.
            if (previous != null) previous.AddFish(zone.Price);

            int index = ZoneIndexById(zoneId);
            Zones[index] = new ZoneState { ZoneId = (byte)zoneId, HasOwner = true, Owner = buyer, Price = price, LockedRound = RoundNumber.Value };
            string news = $"{player.DisplayName} перекупил «{YardZones.NameOf(zoneId)}»" +
                          (previous != null ? $" у {previous.DisplayName}" : "") + $" за {price} рыб!";
            ZoneNews.Value = new FixedString128Bytes(news.Length > 120 ? news.Substring(0, 120) : news);
            ZoneNewsTime.Value = Now;
            Debug.Log("[Maoshi] " + news);
        }

        // Клиент: подсказка у флажка чужой зоны и удержание E.

        /// <summary>Клиент: у какой зоны стоит свой кот (-1 — ни у какой чужой).</summary>
        public int PromptZone { get; private set; } = -1;
        /// <summary>Клиент: прогресс удержания E (0..1).</summary>
        public float BuyoutHoldProgress { get; private set; }
        private float _holdTime;
        private bool _holdSent;
        private bool _voiceMuted;

        private void LocalBuyoutInput()
        {
            int zoneId = -1;
            var nm = NetworkManager.Singleton;
            ulong me = nm.LocalClientId;
            var cat = Cat.CatController.FindByOwner(me);
            if (cat != null && Phase.Value == SessionPhase.Hub && Hub.HubVoting.Current != null && !Cat.CatInput.Blocked)
            {
                var marker = Hub.ZoneMarker.Nearest(cat.transform.position, buyoutRadius);
                if (marker != null && TryGetZone(marker.ZoneId, out var zone) && zone.HasOwner && zone.Owner != me) zoneId = marker.ZoneId;
            }
            PromptZone = zoneId;

            bool mute = zoneId >= 0; // у флажка E — перекуп, а не мяуканье
            if (mute != _voiceMuted) { Audio.CatVoice.Suppressed = mute; _voiceMuted = mute; }

            var keyboard = Maoshi.GameKeyboard.Current;
            bool holding = zoneId >= 0 && keyboard != null && keyboard.eKey.isPressed && CanBuyout(zoneId, me, out _);
            if (!holding)
            {
                _holdTime = 0f;
                _holdSent = false;
                BuyoutHoldProgress = 0f;
                return;
            }
            _holdTime += Time.deltaTime;
            BuyoutHoldProgress = Mathf.Clamp01(_holdTime / buyoutHold);
            if (BuyoutHoldProgress >= 1f && !_holdSent)
            {
                _holdSent = true;
                BuyoutRpc(zoneId);
            }
        }

        // ───────────────────────── Финал ─────────────────────────

        /// <summary>Сервер: собрать номинации из статистики партии.</summary>
        private void BuildNominations()
        {
            var ids = new List<ulong>();
            foreach (var player in NetworkPlayer.All) ids.Add(player.OwnerClientId);
            Nominations.Clear();
            foreach (var nomination in SessionStats.Build(ids)) Nominations.Add(nomination);
            FinalId.Value = Random.Range(1, int.MaxValue);
        }

        /// <summary>Сервер (F8 в редакторе, команда final): сразу финал со случайной статистикой.</summary>
        public bool DebugFinal()
        {
            if (!IsServer || Phase.Value != SessionPhase.Hub) return false;
            foreach (var player in NetworkPlayer.All)
            {
                SessionStats.Add(player.OwnerClientId, Stat.Hits, Random.Range(1, 15));
                SessionStats.Add(player.OwnerClientId, Stat.Stunned, Random.Range(1, 12));
                SessionStats.Add(player.OwnerClientId, Stat.Wins, Random.Range(0, 4));
                SessionStats.Add(player.OwnerClientId, Stat.FishEarned, Random.Range(5, 40));
                SessionStats.Add(player.OwnerClientId, Stat.RouletteBet, Random.Range(0, 20));
                SessionStats.Add(player.OwnerClientId, Stat.Eliminated, Random.Range(0, 5));
            }
            BuildNominations();
            Phase.Value = SessionPhase.Final;
            return true;
        }

        /// <summary>Итоговая таблица: больше зон — выше, при равенстве — больше рыбы.</summary>
        public List<NetworkPlayer> GetStandings()
        {
            var players = new List<NetworkPlayer>(NetworkPlayer.All);
            players.Sort((a, b) =>
            {
                int zones = ZoneCount(b.OwnerClientId).CompareTo(ZoneCount(a.OwnerClientId));
                return zones != 0 ? zones : b.Fish.Value.CompareTo(a.Fish.Value);
            });
            return players;
        }

        /// <summary>Сервер: начать новую партию с теми же игроками.</summary>
        public void RestartSession()
        {
            if (!IsServer || Phase.Value != SessionPhase.Final) return;

            foreach (var player in NetworkPlayer.All) player.AddFish(-player.Fish.Value);
            RoundNumber.Value = 0;
            LastMiniGameId = "";
            _playedHistory.Clear();
            SessionStats.Reset();
            Nominations.Clear();
            CreateZones();
            Phase.Value = SessionPhase.Hub;
        }
    }
}
