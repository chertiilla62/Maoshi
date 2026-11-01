using System.Collections.Generic;
using System.Text;
using Maoshi.Hub;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.Showdown;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Игровой интерфейс поверх двора и мини-игр:
    /// - слева сверху — карточка двора (раунд, игроки, зоны, рыба);
    /// - по центру сверху — баннер (голосование, таймер мини-игры, подсказка);
    /// - по центру — крупные надписи (отсчёт, «Финиш!») и окна (аукцион, разборка, итоги, финал);
    /// - справа сверху — очки; справа снизу — карта двора (видна всегда); слева снизу — подсказка управления.
    ///
    /// Данные читаются каждый кадр из SessionManager / MiniGameMode / HubVoting / ShowdownManager.
    /// Списки пересобираются только когда их содержимое поменялось (по «ключу»), таймеры обновляются каждый кадр.
    /// </summary>
    public class HudScreen
    {
        public VisualElement Root { get; }

        // Слева сверху.
        private readonly VisualElement _infoCard;
        private readonly Label _infoTitle;
        private readonly VisualElement _infoPlayers;
        private string _infoKey = "";

        // Баннер.
        private readonly Label _bannerTitle;
        private readonly Label _bannerSub;
        private readonly Label _bannerWarn;

        // Центр.
        private readonly Label _bigCenter;
        private readonly VisualElement _modal;
        private string _modalKey = "";
        private Label _modalTimer;
        private Label _bidValue;
        private Label _bidStatus;

        // Справа.
        private readonly VisualElement _scores;
        private readonly VisualElement _scoreRows;
        private string _scoresKey = "";
        private readonly VisualElement _zoneMap;
        private readonly VisualElement _zoneGrid;
        private readonly Label _zoneTitle;
        private readonly Label _zoneSummary;
        private bool _zonesCollapsed;
        private string _zonesKey = "";

        // Аукцион: черновик ставки (у каждого игрока свой).
        private int _bidDraft;
        private int _bidSent = -1;
        private int _auctionKey = -1;

        // Свалка: выбор локального игрока в текущем ходу.
        private int _showdownRound = -1;
        private int _pickedIndex = -1;
        private string _pickedName = "";
        private int _selectedCard = -1;
        private VisualElement _bannerPlate;
        private string _lastBig = "";
        private string _ownersKey = "";
        private string _lastModalKind = "none";
        private ShowdownStage _lastBrawlStage = ShowdownStage.Inactive;
        private ulong _selectedTarget = BrawlRules.NoOne;

        // Голосование: «барабан» выбора (для щелчков).
        private int _lastRevealHighlight = -2;

        // Перекуп зоны: подсказка у флажка.
        private readonly VisualElement _buyout;
        private readonly Label _buyoutText;
        private readonly VisualElement _buyoutBar;
        private readonly VisualElement _buyoutFill;

        // Кошачья рулетка.
        private readonly VisualElement _roulette;
        private readonly Label _rouletteStatus;
        private readonly Label _rouletteBets;
        private readonly Label _rouletteNotice;
        private readonly VisualElement _rouletteKeys;

        public HudScreen()
        {
            Root = UIKit.Layout("layer");

            // Карточка двора.
            _infoCard = UIKit.Box("panel", "panel--hud", "hud-top-left");
            _infoTitle = UIKit.Text("", "h2");
            _infoPlayers = UIKit.Layout();
            _infoCard.Add(_infoTitle);
            _infoCard.Add(_infoPlayers);
            Root.Add(_infoCard);

            // Баннер.
            var banner = UIKit.Layout("banner");
            _bannerPlate = UIKit.Layout("banner__plate");
            StickerShadow.Attach(_bannerPlate, 6f);
            _bannerTitle = UIKit.Text("", "banner__title");
            _bannerSub = UIKit.Text("", "banner__sub");
            _bannerWarn = UIKit.Text("", "banner__warn");
            _bannerPlate.Add(_bannerTitle);
            banner.Add(_bannerPlate);
            banner.Add(_bannerSub);
            banner.Add(_bannerWarn);
            Root.Add(banner);

            // Крупная надпись по центру.
            var bigLayer = UIKit.Layout("layer", "center");
            _bigCenter = UIKit.Text("", "big-center");
            bigLayer.Add(_bigCenter);
            Root.Add(bigLayer);

            // Окна по центру (аукцион, разборка, итоги, финал).
            _modal = UIKit.Layout("layer", "center");
            Root.Add(_modal);

            // Кошачья рулетка: справа, над картой двора.
            _roulette = UIKit.Box("panel", "panel--hud", "hud-roulette");
            _roulette.pickingMode = PickingMode.Ignore;
            var rouletteHead = UIKit.Layout("roulette-head");
            rouletteHead.Add(UIKit.Picture("RouletteIcon", "roulette-icon"));
            rouletteHead.Add(UIKit.Text("Кошачья рулетка", "h2"));
            _roulette.Add(rouletteHead);
            _rouletteStatus = UIKit.Text("", "text", "roulette-status");
            _rouletteBets = UIKit.Text("", "muted");
            _rouletteNotice = UIKit.Text("", "roulette-notice");
            _rouletteKeys = UIKit.Layout("keys", "roulette-keys");
            _rouletteKeys.Add(UIKit.Key("E", "+1 рыба"));
            _rouletteKeys.Add(UIKit.Key("Shift+E", "+5"));
            _rouletteKeys.Add(UIKit.Key("Q", "забрать"));
            _roulette.Add(_rouletteStatus);
            _roulette.Add(_rouletteBets);
            _roulette.Add(_rouletteNotice);
            _roulette.Add(_rouletteKeys);
            Root.Add(_roulette);

            // Перекуп зоны.
            _buyout = UIKit.Layout("buyout-layer");
            var buyoutPanel = UIKit.Box("panel", "panel--hud", "buyout-panel");
            buyoutPanel.pickingMode = PickingMode.Ignore;
            _buyoutText = UIKit.Text("", "buyout-text");
            _buyoutBar = UIKit.Layout("buyout-bar");
            _buyoutFill = UIKit.Layout("buyout-fill");
            _buyoutBar.Add(_buyoutFill);
            buyoutPanel.Add(_buyoutText);
            buyoutPanel.Add(_buyoutBar);
            _buyout.Add(buyoutPanel);
            Root.Add(_buyout);

            // Очки.
            _scores = UIKit.Box("panel", "panel--hud", "hud-top-right");
            _scores.Add(UIKit.Text("Очки", "h2"));
            _scoreRows = UIKit.Layout();
            _scores.Add(_scoreRows);
            Root.Add(_scores);

            // Карта двора.
            // Зоны двора: компактный список «● Зона — хозяин». M — свернуть/развернуть.
            _zoneMap = UIKit.Box("panel", "panel--hud", "hud-bottom-right", "zone-map");
            var mapHead = UIKit.Layout("zone-map__head");
            _zoneTitle = UIKit.Text("Зоны двора", "zone-map__title");
            mapHead.Add(_zoneTitle);
            mapHead.Add(UIKit.Layout("grow"));
            mapHead.Add(UIKit.Text("M", "keycap", "zone-map__key"));
            _zoneMap.Add(mapHead);
            _zoneSummary = UIKit.Text("", "zone-map__summary");
            _zoneMap.Add(_zoneSummary);
            _zoneGrid = UIKit.Layout("zone-list");
            _zoneMap.Add(_zoneGrid);
            Root.Add(_zoneMap);

            // Подсказка управления.
            var hint = UIKit.Layout("hud-bottom-left", "keys");
            hint.Add(UIKit.Key("WASD", "бег"));
            hint.Add(UIKit.Key("C", "красться"));
            hint.Add(UIKit.Key("Пробел", "прыжок"));
            hint.Add(UIKit.Key("ЛКМ", "толкнуть"));
            hint.Add(UIKit.Key("Колёсико", "ближе / дальше"));
            hint.Add(UIKit.Key("E", "мяу"));
            hint.Add(UIKit.Key("Q", "шипеть"));
            hint.Add(UIKit.Key("Alt", "курсор"));
            hint.Add(UIKit.Key("Esc", "меню"));
            Root.Add(hint);
        }

        public void Refresh()
        {
            var session = SessionManager.Instance;
            var mode = MiniGameMode.Current;
            var showdown = ShowdownManager.Instance;
            var voting = HubVoting.Current;

            bool inMiniGame = mode != null;
            bool results = session != null && session.ResultsVisible;
            bool final = session != null && session.Phase.Value == SessionPhase.Final;

            // Карточка двора — только во дворе.
            UIKit.Show(_infoCard, !inMiniGame && !results && !final);
            if (!inMiniGame) RefreshInfoCard(session);

            RefreshZoneMap(session);
            RefreshScores(mode);

            // Баннер и крупная надпись.
            string title = "", sub = "", warn = "", big = "";
            bool bigIsTitle = false;

            if (!results && !final)
            {
                if (inMiniGame) DescribeMiniGame(mode, ref title, ref sub, ref warn, ref big);
                else if (voting != null && (showdown == null || !showdown.IsActive) &&
                         (session == null || session.Phase.Value != SessionPhase.Auction))
                {
                    if (voting.Phase.Value == VotePhase.Voting)
                    {
                        title = $"Выбор игры: {Mathf.CeilToInt(voting.TimeLeft)} c";
                        ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
                        int myPad = voting.PadOf(me);
                        if (myPad >= 0) sub = "Ты за: " + HubVoting.TitleOf(voting.OptionIds[myPad].ToString());
                        else if (voting.TimeLeft <= 10f) warn = "Ты ещё не выбрал — встань котом на круг у афиши!";
                        else sub = "Встань котом на круг у афиши с игрой";
                    }
                    else if (voting.Phase.Value == VotePhase.Announcing)
                    {
                        int highlight = voting.RevealHighlight(out bool settled);
                        if (!settled)
                        {
                            title = "Выбираем игру…";
                            if (highlight != _lastRevealHighlight && _lastRevealHighlight != -2)
                                Maoshi.Audio.AudioManager.Play("tick", 0.8f, 0.05f);
                        }
                        else
                        {
                            big = (voting.WinnerIsRandom ? "Кот в мешке: " : "Играем: ") + voting.WinnerTitle + "!";
                            bigIsTitle = true;
                        }
                        _lastRevealHighlight = highlight;
                    }
                }
            }

            if (voting == null || voting.Phase.Value != VotePhase.Announcing) _lastRevealHighlight = -2;
            RefreshBuyout(session, inMiniGame || results || final);
            if (!inMiniGame && !results && !final && warn.Length == 0 && session != null &&
                NetworkManager.Singleton != null && NetworkManager.Singleton.ServerTime.Time - session.ZoneNewsTime.Value < 5.0)
                warn = session.ZoneNews.Value.ToString();
            RefreshRoulette(session, inMiniGame || results || final || (showdown != null && showdown.IsActive) ||
                                     (session != null && session.Phase.Value == SessionPhase.Auction));

            UIKit.SetText(_bannerTitle, title);
            UIKit.SetText(_bannerSub, sub);
            UIKit.SetText(_bannerWarn, warn);
            UIKit.Show(_bannerTitle, title.Length > 0);
            UIKit.Show(_bannerPlate, title.Length > 0);
            UIKit.Show(_bannerSub, sub.Length > 0);
            UIKit.Show(_bannerWarn, warn.Length > 0);
            if (big != _lastBig)
            {
                // Звуки крупных надписей: отсчёт, старт, финиш, выбор игры.
                if (big.Length == 1 && char.IsDigit(big[0])) Maoshi.Audio.AudioManager.Play("countdown");
                else if (big == "Вперёд!") Maoshi.Audio.AudioManager.Play("go");
                else if (big == "Финиш!") Maoshi.Audio.AudioManager.Play("whistle");
                else if (big.StartsWith("Играем")) Maoshi.Audio.AudioManager.Play("announce");
                else if (big.StartsWith("Кот в мешке")) { Maoshi.Audio.AudioManager.Play("card_reveal"); Maoshi.Audio.AudioManager.Play("announce"); }
                _lastBig = big;
            }
            UIKit.SetText(_bigCenter, big);
            _bigCenter.EnableInClassList("big-center--title", bigIsTitle);
            UIKit.Show(_bigCenter, big.Length > 0);

            // Окно по центру. Во время свалки двор затемняем — стол на асфальте.
            if (!final) { _finalSince = -1f; _shownNominations = -1; }
            if (results) RefreshResults(session);
            else if (final) RefreshFinal(session);
            else if (!inMiniGame && showdown != null && showdown.IsActive) RefreshShowdown(showdown);
            else if (!inMiniGame && session != null && session.Phase.Value == SessionPhase.Auction) RefreshAuction(session);
            else if (inMiniGame && mode is IMiniGameModal custom && !string.IsNullOrEmpty(custom.ModalKey)) SetModal("mg:" + custom.ModalKey, custom.BuildModal);
            else SetModal("none", null);
        }

        // ───────────────────────── Двор ─────────────────────────

        private void RefreshInfoCard(SessionManager session)
        {
            string round = session != null ? $"Двор, раунд {session.RoundNumber.Value} из {session.TotalRounds.Value}" : "Двор";
            UIKit.SetText(_infoTitle, round);

            var key = new StringBuilder();
            foreach (var player in NetworkPlayer.All)
                key.Append(player.OwnerClientId).Append(player.DisplayName).Append(player.ColorIndex.Value)
                   .Append(':').Append(session != null ? session.ZoneCount(player.OwnerClientId) : 0)
                   .Append(':').Append(player.Fish.Value).Append('|');
            if (key.ToString() == _infoKey) return;
            _infoKey = key.ToString();

            _infoPlayers.Clear();
            foreach (var player in NetworkPlayer.All)
            {
                bool me = player == NetworkPlayer.Local;
                var row = UIKit.Layout("player-row");
                if (me) row.AddToClassList("player-row--me");
                row.Add(UIKit.Swatch(player.Color));
                row.Add(UIKit.Text(player.DisplayName + (me ? " (ты)" : ""), "text", "grow"));
                int zones = session != null ? session.ZoneCount(player.OwnerClientId) : 0;
                row.Add(UIKit.Text($"зон {zones}", "muted"));
                var fishInfo = UIKit.Fish(player.Fish.Value, 22f, "text");
                fishInfo.style.marginLeft = 12;
                row.Add(fishInfo);
                _infoPlayers.Add(row);
            }
        }

        // ───────────────────────── Перекуп зоны ─────────────────────────

        private void RefreshBuyout(SessionManager session, bool hidden)
        {
            int zoneId = session != null && !hidden ? session.PromptZone : -1;
            ZoneState zone = default;
            bool show = zoneId >= 0 && session.TryGetZone(zoneId, out zone) && zone.HasOwner;
            UIKit.Show(_buyout, show);
            if (!show) return;

            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            var owner = NetworkPlayer.FindByClientId(zone.Owner);
            int price = session.BuyoutPrice(zone);
            string head = $"«{YardZones.NameOf(zoneId)}» — у {(owner != null ? owner.DisplayName : "?")} (куплена за {zone.Price})";
            string line = session.CanBuyout(zoneId, me, out string reason)
                ? $"Держи E — перекупить за {price} рыб (+30%)"
                : reason;
            UIKit.SetText(_buyoutText, head + "\n" + line);
            UIKit.Show(_buyoutBar, session.BuyoutHoldProgress > 0f);
            _buyoutFill.style.width = Length.Percent(session.BuyoutHoldProgress * 100f);
        }

        // ───────────────────────── Кошачья рулетка ─────────────────────────

        private void RefreshRoulette(SessionManager session, bool hidden)
        {
            var roulette = CatRoulette.Current;
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            bool mine = roulette != null && roulette.TotalBetOf(me) > 0;
            bool show = !hidden && roulette != null && roulette.IsSpawned &&
                        (roulette.LocalDistance < 8f || mine);
            UIKit.Show(_roulette, show);
            if (!show) return;

            UIKit.SetText(_rouletteStatus, roulette.StatusText);

            string bets = roulette.BetsText(me);
            int won = roulette.WinningsOf(me);
            string betsLine;
            if (roulette.Outcome != null && mine) betsLine = won > 0 ? $"Ты выиграл {won} рыб!  ({bets})" : $"Мимо…  ({bets})";
            else betsLine = bets.Length > 0 ? "Твои ставки: " + bets : "Ставок нет. Рыжие и серые ×2, золотая ×10, пёс забирает всё.";
            UIKit.SetText(_rouletteBets, betsLine);
            _rouletteBets.EnableInClassList("roulette-win", roulette.Outcome != null && won > 0);

            bool notice = Time.time < roulette.NoticeUntil;
            UIKit.SetText(_rouletteNotice, notice ? roulette.Notice : "");
            UIKit.Show(_rouletteNotice, notice);
            UIKit.Show(_rouletteKeys, roulette.LocalMat >= 0);
        }

        // ───────────────────────── Мини-игра ─────────────────────────

        private static void DescribeMiniGame(MiniGameMode mode, ref string title, ref string sub, ref string warn, ref string big)
        {
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            switch (mode.Phase)
            {
                case MiniGamePhase.WaitingForPlayers:
                    title = "Ждём остальных…";
                    break;

                case MiniGamePhase.Countdown:
                    int seconds = Mathf.CeilToInt(mode.TimeLeft);
                    big = seconds > 0 ? seconds.ToString() : "Вперёд!";
                    sub = mode.StatusText;
                    break;

                case MiniGamePhase.Playing:
                    string timer = mode.TimeLeft > 0f ? $"{Mathf.CeilToInt(mode.TimeLeft)} c" : "";
                    string alive = mode.HasScores ? "" : $"в игре: {mode.AliveCount}";
                    title = timer.Length > 0 && alive.Length > 0 ? $"{timer}   ·   {alive}" : timer + alive;
                    sub = mode.StatusText;
                    var spectator = SpectatorSystem.Instance;
                    if (spectator != null && spectator.IsSpectating)
                    {
                        float cd = spectator.ThrowCooldownLeft;
                        string slipper = cd > 0f ? $"тапок через {Mathf.CeilToInt(cd)} с" : "R — кинуть тапок!";
                        warn = $"Ты выбыл и смотришь за: {spectator.WatchedName}   ·   Tab — другой кот   ·   {slipper}";
                    }
                    else if (!mode.IsAlive(localId)) warn = "Ты выбыл — болей за остальных!";
                    else if (spectator != null && Time.time < spectator.VictimNoticeUntil) warn = spectator.VictimNotice;
                    break;

                case MiniGamePhase.Finished:
                    big = "Финиш!";
                    break;
            }
        }

        private void RefreshScores(MiniGameMode mode)
        {
            bool visible = mode != null && mode.HasScores;
            UIKit.Show(_scores, visible);
            if (!visible) return;

            var entries = new List<PlayerScore>();
            foreach (var score in mode.Scores) entries.Add(score);
            entries.Sort((a, b) => b.Score.CompareTo(a.Score));

            var key = new StringBuilder();
            foreach (var entry in entries) key.Append(entry.ClientId).Append(':').Append(entry.Score).Append('|');
            if (key.ToString() == _scoresKey) return;
            _scoresKey = key.ToString();

            _scoreRows.Clear();
            ulong localId = NetworkManager.Singleton.LocalClientId;
            foreach (var entry in entries)
            {
                var row = UIKit.Layout("score-row");
                row.Add(UIKit.Text(entry.Score.ToString(), "score-value"));
                var player = NetworkPlayer.FindByClientId(entry.ClientId);
                if (player != null) row.Add(UIKit.Swatch(player.Color));
                row.Add(UIKit.Text(NameOf(entry.ClientId) + (entry.ClientId == localId ? " (ты)" : ""), "text"));
                _scoreRows.Add(row);
            }
        }

        // ───────────────────────── Карта двора ─────────────────────────

        private void RefreshZoneMap(SessionManager session)
        {
            bool visible = session != null && session.Zones.Count > 0;
            UIKit.Show(_zoneMap, visible);
            if (!visible) return;

            // Хозяин зоны сменился — «тын-дын».
            var owners = new StringBuilder();
            foreach (var zone in session.Zones) owners.Append(zone.HasOwner ? zone.Owner.ToString() : "-").Append(',');
            string ownersKey = owners.ToString();
            if (_ownersKey.Length > 0 && ownersKey != _ownersKey) Maoshi.Audio.AudioManager.Play("zone");
            _ownersKey = ownersKey;

            // M — свернуть/развернуть список (свёрнутый — только заголовок со счётом).
            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                _zonesCollapsed = !_zonesCollapsed;
                _zonesKey = "";
            }

            int auctionZone = session.Phase.Value == SessionPhase.Auction ? session.AuctionZone.Value : -1;
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            var key = new StringBuilder().Append(auctionZone).Append(_zonesCollapsed).Append('|');
            foreach (var zone in session.Zones)
            {
                key.Append(zone.ZoneId).Append(':').Append(zone.HasOwner ? zone.Owner.ToString() : "-").Append(':').Append(zone.Price);
                var owner = zone.HasOwner ? NetworkPlayer.FindByClientId(zone.Owner) : null;
                key.Append(':').Append(owner != null ? owner.ColorIndex.Value + owner.DisplayName : "-").Append('|');
            }
            if (key.ToString() == _zonesKey) return;
            _zonesKey = key.ToString();

            int free = 0, mine = 0;
            foreach (var zone in session.Zones)
            {
                if (!zone.HasOwner) free++;
                else if (zone.Owner == me) mine++;
            }
            UIKit.SetText(_zoneSummary, $"твоих {mine}  ·  свободно {free}  ·  всего {session.Zones.Count}");
            UIKit.Show(_zoneGrid, !_zonesCollapsed);

            _zoneGrid.Clear();
            foreach (var zone in session.Zones)
            {
                var owner = zone.HasOwner ? NetworkPlayer.FindByClientId(zone.Owner) : null;
                var row = UIKit.Layout("zone-row");
                if (owner == null) row.AddToClassList("zone-row--free");
                else if (owner.OwnerClientId == me) row.AddToClassList("zone-row--mine");
                if (zone.ZoneId == auctionZone) row.AddToClassList("zone-row--auction");

                var dot = UIKit.Layout("zone-row__dot");
                if (owner != null) dot.style.backgroundColor = owner.Color;
                row.Add(dot);
                row.Add(UIKit.Text(YardZones.NameOf(zone.ZoneId), "zone-row__name"));
                row.Add(UIKit.Layout("grow"));
                string who = zone.ZoneId == auctionZone ? "аукцион!" : owner == null ? "свободна" : owner.OwnerClientId == me ? "ты" : owner.DisplayName;
                row.Add(UIKit.Text(who, "zone-row__owner"));
                if (owner != null) row.Add(UIKit.Fish(zone.Price, 13f, "zone-row__price"));
                _zoneGrid.Add(row);
            }
        }

        // ───────────────────────── Окна по центру ─────────────────────────

        /// <summary>Показать окно. Пересобирается, только если поменялся ключ.</summary>
        private void SetModal(string key, System.Func<VisualElement> build)
        {
            if (key == _modalKey) return;
            _modalKey = key;
            string kind = key.Contains(':') ? key.Substring(0, key.IndexOf(':')) : key;
            if (kind != _lastModalKind)
            {
                switch (kind)
                {
                    case "auction": Maoshi.Audio.AudioManager.Play("gavel"); break;
                    case "results": Maoshi.Audio.AudioManager.Play("results"); break;
                    case "final": Maoshi.Audio.AudioManager.Play("fanfare"); break;
                    case "brawl": Maoshi.Audio.AudioManager.Play("hiss", 0.8f); break;
                }
                _lastModalKind = kind;
            }
            _modal.Clear();
            _modalTimer = null;
            _bidValue = null;
            _bidStatus = null;
            if (build != null) _modal.Add(build());
            // Окно с кнопками — освобождаем курсор (иначе он заперт камерой).
            Maoshi.Cat.CatInput.UIWantsCursor = _modal.Q<Button>() != null;
        }

        // Аукцион.

        private void RefreshAuction(SessionManager session)
        {
            int zoneId = session.AuctionZone.Value;
            if (zoneId < 0 || session.Auction.Value == AuctionStage.None)
            {
                SetModal("none", null);
                return;
            }

            int auctionKey = session.RoundNumber.Value * 100 + zoneId;
            if (auctionKey != _auctionKey)
            {
                _auctionKey = auctionKey;
                _bidDraft = 0;
                _bidSent = -1;
            }

            bool bidding = session.Auction.Value == AuctionStage.Bidding;
            var key = new StringBuilder("auction:").Append(auctionKey).Append(':').Append(session.Auction.Value);
            if (!bidding)
            {
                foreach (var bid in session.RevealedBids) key.Append(bid.ClientId).Append('=').Append(bid.Score).Append(',');
                key.Append(session.AuctionWinner.Value).Append(':').Append(session.AuctionPrice.Value);
            }

            SetModal(key.ToString(), () => BuildAuction(session, zoneId, bidding));

            // Каждый кадр — таймер, ставка, сколько поставили.
            var local = NetworkPlayer.Local;
            int fish = local != null ? local.Fish.Value : 0;
            _bidDraft = Mathf.Clamp(_bidDraft, 0, fish);
            if (_modalTimer != null)
                UIKit.SetText(_modalTimer, bidding
                    ? $"Ставки тайные · осталось {Mathf.CeilToInt(session.AuctionTimeLeft)} c · платит только победитель"
                    : "Ставки открыты!");
            if (_bidValue != null) UIKit.SetText(_bidValue, _bidDraft.ToString());
            if (_bidStatus != null)
            {
                string mine = _bidSent >= 0 ? $"Твоя ставка: {_bidSent} · " : "";
                UIKit.SetText(_bidStatus, $"{mine}у тебя рыбы: {fish} · поставили {session.BidPlaced.Count} из {NetworkPlayer.All.Count}");
            }
        }

        private VisualElement BuildAuction(SessionManager session, int zoneId, bool bidding)
        {
            var panel = UIKit.Box("panel");
            panel.Add(UIKit.Text("Аукцион: " + YardZones.NameOf(zoneId), "h1"));

            bool hasOwner = session.TryGetZoneOwner(zoneId, out ulong owner);
            panel.Add(UIKit.Text(hasOwner
                ? $"Владелец: {NameOf(owner)} (защита +{session.DefenderBonus} к ставке)"
                : "Зона свободна", "text"));

            _modalTimer = UIKit.Text("", "muted");
            panel.Add(_modalTimer);
            panel.Add(UIKit.Spacer());

            if (bidding)
            {
                var row = UIKit.Row();
                row.style.justifyContent = Justify.Center;
                row.Add(UIKit.Button("−5", () => _bidDraft -= 5, "btn--square"));
                row.Add(UIKit.Button("−1", () => _bidDraft -= 1, "btn--square"));
                _bidValue = UIKit.Text("0", "big-center");
                _bidValue.style.fontSize = 54;
                _bidValue.style.width = 120;
                row.Add(_bidValue);
                row.Add(UIKit.FishImage(44f));
                row.Add(UIKit.Button("+1", () => _bidDraft += 1, "btn--square"));
                row.Add(UIKit.Button("+5", () => _bidDraft += 5, "btn--square"));
                panel.Add(row);

                panel.Add(UIKit.Button("Поставить", () =>
                {
                    session.SubmitBid(_bidDraft);
                    Maoshi.Audio.AudioManager.Play("bid");
                    _bidSent = _bidDraft;
                }, "btn--primary"));

                _bidStatus = UIKit.Text("", "muted");
                _bidStatus.style.marginTop = 8;
                panel.Add(_bidStatus);
            }
            else
            {
                var bids = new List<PlayerScore>();
                foreach (var bid in session.RevealedBids) bids.Add(bid);
                bids.Sort((a, b) => b.Score.CompareTo(a.Score));

                if (bids.Count == 0) panel.Add(UIKit.Text("Никто не поставил.", "text"));
                foreach (var bid in bids)
                {
                    var row = UIKit.Layout("player-row");
                    var player = NetworkPlayer.FindByClientId(bid.ClientId);
                    if (player != null) row.Add(UIKit.Swatch(player.Color));
                    row.Add(UIKit.Text(NameOf(bid.ClientId), "text", "grow"));
                    row.Add(UIKit.Fish(bid.Score));
                    panel.Add(row);
                }

                panel.Add(UIKit.Spacer());
                ulong winner = session.AuctionWinner.Value;
                panel.Add(UIKit.Text(winner == SessionManager.NoOne
                    ? "Зона остаётся свободной."
                    : $"{NameOf(winner)} забирает зону за {session.AuctionPrice.Value} рыбы!", "h2", "accent"));
            }

            return panel;
        }

        // Кошачья свалка.

        private void RefreshShowdown(ShowdownManager brawl)
        {
            if (brawl.Stage.Value != _lastBrawlStage)
            {
                if (brawl.Stage.Value == ShowdownStage.Reveal) Maoshi.Audio.AudioManager.Play("card_reveal");
                else if (brawl.Stage.Value == ShowdownStage.Finished)
                {
                    bool won = NetworkManager.Singleton != null && brawl.Winner.Value == NetworkManager.Singleton.LocalClientId;
                    Maoshi.Audio.AudioManager.Play(won ? "win" : "lose");
                }
                _lastBrawlStage = brawl.Stage.Value;
            }
            if (brawl.Round.Value != _showdownRound || brawl.Stage.Value != ShowdownStage.Picking)
            {
                if (brawl.Round.Value != _showdownRound) _pickedIndex = -1;
                _showdownRound = brawl.Round.Value;
                if (brawl.Stage.Value != ShowdownStage.Picking) { _selectedCard = -1; _selectedTarget = BrawlRules.NoOne; }
            }

            ulong local = NetworkManager.Singleton.LocalClientId;
            var key = new StringBuilder("brawl:")
                .Append(brawl.Round.Value).Append(brawl.Stage.Value).Append(brawl.ZoneId.Value)
                .Append(brawl.GiftText.Value).Append(brawl.GiftsUsed.Contains(local))
                .Append('|').Append(_selectedCard).Append(':').Append(_selectedTarget).Append(':').Append(_pickedIndex).Append('|');
            foreach (var f in brawl.Fighters)
                key.Append(f.ClientId).Append(f.Lives).Append(f.Alive).Append(f.Picked).Append(f.Fleas)
                   .Append(f.BoxLocked).Append(f.AllyId).Append(f.Shown).Append(f.ShownTarget).Append(',');
            foreach (var line in brawl.Log) key.Append(line).Append(';');
            foreach (var card in brawl.LocalHand) key.Append((int)card).Append(',');

            SetModal(key.ToString(), () => BuildShowdown(brawl, local));

            if (_modalTimer != null)
                UIKit.SetText(_modalTimer, brawl.Stage.Value == ShowdownStage.Picking ? Mathf.CeilToInt(brawl.TimeLeft).ToString() : "");
        }

        private VisualElement BuildShowdown(ShowdownManager brawl, ulong local)
        {
            bool isFighter = brawl.TryGetFighter(local, out var me);
            bool canPlay = isFighter && me.Alive && brawl.Stage.Value == ShowdownStage.Picking && !me.Picked;
            var hand = brawl.LocalHand;
            if (_selectedCard >= hand.Count) _selectedCard = -1;
            ShowdownCard selected = canPlay && _selectedCard >= 0 ? hand[_selectedCard] : ShowdownCard.None;
            bool choosingTarget = selected != ShowdownCard.None && BrawlRules.Info(selected).NeedsTarget;

            // Стол свалки: всё прямо поверх затемнённого двора, без общей панели.
            _modal.Add(UIKit.Layout("layer", "brawl-layer"));
            _modal.Add(UIKit.Picture("ChalkRing", "brawl-ring"));
            var panel = UIKit.Box("panel", "panel--brawl");

            // Заголовок: печать-таймер, название, ход.
            var header = UIKit.Layout("brawl-header");
            _modalTimer = UIKit.Text("", "brawl-timer");
            header.Add(_modalTimer);
            StickerShadow.Attach(_modalTimer, 5f);
            var titles = UIKit.Layout();
            string zone = brawl.ZoneId.Value >= 0 ? "Свалка за «" + YardZones.NameOf(brawl.ZoneId.Value) + "»" : "Тренировочная свалка";
            titles.Add(UIKit.Text(zone, "brawl-title"));
            string stage = brawl.Stage.Value switch
            {
                ShowdownStage.Picking => canPlay ? (choosingTarget ? "кликни по коту-цели" : "выбери карту") : "ждём остальных",
                ShowdownStage.Reveal => "вскрываемся!",
                ShowdownStage.Finished => "свалка окончена",
                _ => ""
            };
            titles.Add(UIKit.Text($"ход {brawl.Round.Value} из {BrawlRules.MaxTurns} — {stage}", "brawl-sub", "chalk"));
            header.Add(titles);
            panel.Add(header);

            // Соперники.
            var seats = UIKit.Layout("seats");
            foreach (var f in brawl.Fighters)
                seats.Add(BuildSeat(brawl, f, local, choosingTarget && canPlay));
            panel.Add(seats);

            // Середина: слева выбор и «Сыграть», справа журнал.
            var middle = UIKit.Layout("brawl-middle");
            var left = UIKit.Layout("brawl-actions");
            if (isFighter && me.Alive && brawl.Stage.Value == ShowdownStage.Picking)
            {
                if (me.Picked)
                {
                    string played = _pickedIndex >= 0 ? _pickedName : "карту";
                    var done = UIKit.Box("brawl-choice");
                    done.Add(UIKit.Text("Твой ход сделан", "muted"));
                    done.Add(UIKit.Text(played, "h2"));
                    done.Add(UIKit.Text("ждём остальных…", "chalk"));
                    StickerShadow.Attach(done, 6f);
                    left.Add(done);
                }
                else BuildPlayRow(left, brawl, hand, me);
            }
            middle.Add(left);

            var log = UIKit.Box("brawl-log");
            log.Add(UIKit.Text("что было в прошлый ход", "brawl-log__title", "chalk"));
            foreach (var line in brawl.Log) log.Add(UIKit.Text(line.ToString(), "brawl-log__line"));
            string gift = brawl.GiftText.Value.ToString();
            if (!string.IsNullOrEmpty(gift)) log.Add(UIKit.Text(gift, "brawl-log__line", "chalk--light"));
            if (brawl.Log.Count == 0 && string.IsNullOrEmpty(gift)) log.Add(UIKit.Text("пока тихо", "brawl-log__line"));
            middle.Add(log);
            panel.Add(middle);

            // Рука.
            if (canPlay)
            {
                var cards = UIKit.Layout("brawl-hand");
                for (int i = 0; i < hand.Count; i++) cards.Add(BuildCard(hand[i], i, hand.Count, me));
                panel.Add(cards);
            }
            else if ((!isFighter || !me.Alive) && brawl.Stage.Value != ShowdownStage.Finished)
            {
                AddGiftButtons(panel, brawl, local);
            }

            return panel;
        }

        private VisualElement BuildSeat(ShowdownManager brawl, FighterState f, ulong local, bool targeting)
        {
            bool me = f.ClientId == local;
            bool validTarget = targeting && f.Alive && !me;
            bool isTarget = f.ClientId == _selectedTarget && targeting;

            var seat = new Button(() =>
            {
                if (!validTarget) return;
                _selectedTarget = f.ClientId;
                _modalKey = ""; // перерисовать
            });
            seat.AddToClassList("seat");
            if (me) seat.AddToClassList("seat--me");
            if (!f.Alive) seat.AddToClassList("seat--out");
            if (validTarget) seat.AddToClassList("seat--selectable");
            if (isTarget) seat.AddToClassList("seat--target");
            seat.SetEnabled(validTarget || !targeting);

            var head = UIKit.Layout("seat__head");
            var player = NetworkPlayer.FindByClientId(f.ClientId);
            var avatar = UIKit.Layout("seat__avatar");
            avatar.style.backgroundColor = player != null ? player.Color : new Color(0.55f, 0.55f, 0.6f);
            head.Add(avatar);
            head.Add(UIKit.Text(me ? "Ты" : ShowdownManager.NameOf(f.ClientId), "seat__name"));
            seat.Add(head);

            // Жизни — лапки.
            var lives = UIKit.Layout("seat__lives");
            for (int i = 0; i < Mathf.Max(BrawlRules.StartLives, f.Lives); i++)
                lives.Add(UIKit.Picture(i < f.Lives ? "PawFull" : "PawEmpty", "seat__life"));
            seat.Add(lives);

            string status;
            if (!f.Alive) status = "выбыл";
            else if (brawl.Stage.Value == ShowdownStage.Picking) status = f.Picked ? "выбрал карту" : "думает…";
            else status = "";
            if (status.Length > 0) seat.Add(UIKit.Text(status, "seat__status", "chalk", f.Picked && f.Alive ? "good" : "muted"));

            var tags = UIKit.Layout("seat__tags");
            if (f.ShownCard != ShowdownCard.None)
            {
                string target = f.ShownTarget != BrawlRules.NoOne ? " → " + ShowdownManager.NameOf(f.ShownTarget) : "";
                tags.Add(UIKit.Text(BrawlRules.NameOf(f.ShownCard) + target, "seat__played", TypeClass(BrawlRules.Info(f.ShownCard).Type)));
            }
            if (f.Fleas > 0) tags.Add(UIKit.Text($"блохи, ещё {f.Fleas}", "seat__badge", "seat__badge--trick"));
            if (f.AllyId != BrawlRules.NoOne) tags.Add(UIKit.Text("союз с " + ShowdownManager.NameOf(f.AllyId), "seat__badge", "seat__badge--care"));
            if (f.BoxLocked && f.Alive) tags.Add(UIKit.Text("без атак", "seat__badge", "seat__badge--defense"));
            if (brawl.Stage.Value == ShowdownStage.Finished && brawl.Winner.Value == f.ClientId)
                tags.Add(UIKit.Text("победитель", "seat__badge", "seat__badge--win"));
            seat.Add(tags);

            if (isTarget) seat.Add(UIKit.Text("цель", "target-flag", "chalk"));

            foreach (var child in seat.Children()) child.pickingMode = PickingMode.Ignore;
            return seat;
        }

        private VisualElement BuildCard(ShowdownCard card, int index, int count, FighterState me)
        {
            var info = BrawlRules.Info(card);
            bool blocked = me.BoxLocked && BrawlRules.IsAttack(card);

            var button = new Button(() =>
            {
                _selectedCard = index;
                Maoshi.Audio.AudioManager.Play("card_pick");
                if (!BrawlRules.Info(card).NeedsTarget) _selectedTarget = BrawlRules.NoOne;
                _modalKey = "";
            });
            button.AddToClassList("bcard");
            button.AddToClassList(TypeClass(info.Type));
            if (index == _selectedCard) button.AddToClassList("bcard--selected");
            button.SetEnabled(!blocked);
            // Веер: карты по краям чуть повёрнуты и ниже.
            float t = count > 1 ? index / (float)(count - 1) * 2f - 1f : 0f;
            button.style.rotate = new Rotate(t * 8f);
            if (index != _selectedCard) button.style.translate = new Translate(0, Mathf.Abs(t) * 16f);

            var top = UIKit.Layout("bcard__top");
            var plate = UIKit.Layout("bcard__icon-plate");
            plate.Add(UIKit.Picture("Card_" + TypeKey(info.Type), "bcard__icon"));
            top.Add(plate);
            button.Add(top);
            button.Add(UIKit.Text(info.Power, "bcard__badge"));
            string target = info.NeedsTarget ? "на кота" : card == ShowdownCard.TrashCan ? "всем" : "";
            if (target.Length > 0) button.Add(UIKit.Text(target, "bcard__target"));

            var body = UIKit.Layout("bcard__body");
            string color = TypeColor(info.Type);
            body.Add(UIKit.Text(info.Name, "bcard__name"));
            body.Add(UIKit.Text(Colorize(info.Main, color), "bcard__main"));
            if (!string.IsNullOrEmpty(info.Sub)) body.Add(UIKit.Text(Colorize(info.Sub, color), "bcard__sub"));
            button.Add(body);

            var footer = UIKit.Layout("bcard__footer");
            footer.Add(UIKit.Text(TypeName(info.Type), "bcard__type", "chalk"));
            if (blocked) footer.Add(UIKit.Text("нельзя", "bcard__type", "chalk"));
            button.Add(footer);

            foreach (var child in button.Children()) child.pickingMode = PickingMode.Ignore;
            return button;
        }

        private void BuildPlayRow(VisualElement parent, ShowdownManager brawl, IReadOnlyList<ShowdownCard> hand, FighterState me)
        {
            string title, detail;
            bool ready = false;

            if (_selectedCard < 0)
            {
                title = "Выбери карту";
                detail = me.BoxLocked ? "после коробки атаковать нельзя" : "внизу твоя рука";
            }
            else
            {
                var card = hand[_selectedCard];
                var info = BrawlRules.Info(card);
                title = info.Name;
                if (info.NeedsTarget && _selectedTarget == BrawlRules.NoOne)
                    detail = "кликни по коту-цели";
                else
                {
                    detail = info.NeedsTarget ? "на " + ShowdownManager.NameOf(_selectedTarget) : "можно играть";
                    ready = true;
                }
            }

            var choice = UIKit.Box("brawl-choice");
            choice.Add(UIKit.Text("Твой ход", "muted"));
            choice.Add(UIKit.Text(title, "h2"));
            choice.Add(UIKit.Text(detail, "text"));
            StickerShadow.Attach(choice, 6f);
            parent.Add(choice);

            var play = UIKit.Button("Сыграть", () =>
            {
                if (_selectedCard < 0 || _selectedCard >= hand.Count) return;
                var card = hand[_selectedCard];
                ulong target = BrawlRules.Info(card).NeedsTarget ? _selectedTarget : BrawlRules.NoOne;
                brawl.PickCard(_selectedCard, target);
                Maoshi.Audio.AudioManager.Play("card_play");
                _pickedIndex = _selectedCard;
                _pickedName = BrawlRules.NameOf(card) + (target != BrawlRules.NoOne ? " → " + ShowdownManager.NameOf(target) : "");
                _selectedCard = -1;
                _selectedTarget = BrawlRules.NoOne;
                _modalKey = "";
            }, "btn--primary");
            play.style.marginTop = 14;
            play.SetEnabled(ready);
            parent.Add(play);
        }

        private static void AddGiftButtons(VisualElement panel, ShowdownManager brawl, ulong local)
        {
            if (brawl.GiftsUsed.Contains(local))
            {
                panel.Add(UIKit.Text("Ты уже подкинул карту в этой свалке.", "text", "gift-note"));
                return;
            }

            panel.Add(UIKit.Text("Подкинь случайную карту одному из бойцов — один раз за свалку:", "text", "gift-note"));
            var row = UIKit.Layout("gift-row");
            foreach (var f in brawl.Fighters)
            {
                if (!f.Alive) continue;
                ulong id = f.ClientId;
                var button = UIKit.Button("Помочь: " + ShowdownManager.NameOf(id), () => brawl.Gift(id), "btn--small");
                button.style.marginRight = 8;
                row.Add(button);
            }
            panel.Add(row);
        }

        private static string TypeKey(CardType type) => type switch
        {
            CardType.Attack => "attack",
            CardType.Defense => "defense",
            CardType.Trick => "trick",
            _ => "care",
        };

        private static string TypeClass(CardType type) => type switch
        {
            CardType.Attack => "type-attack",
            CardType.Defense => "type-defense",
            CardType.Trick => "type-trick",
            _ => "type-care",
        };

        private static string TypeColor(CardType type) => type switch
        {
            CardType.Attack => "#B4501A",
            CardType.Defense => "#17796E",
            CardType.Trick => "#6A4FC0",
            _ => "#4C8A2A",
        };

        private static string TypeName(CardType type) => type switch
        {
            CardType.Attack => "атака",
            CardType.Defense => "защита",
            CardType.Trick => "хитрость",
            _ => "забота",
        };

        /// <summary>Выделенные &lt;b&gt; числа красим в цвет типа карты.</summary>
        private static string Colorize(string text, string color) =>
            text.Replace("<b>", $"<b><color={color}>").Replace("</b>", "</color></b>");

        // Итоги мини-игры.

        private void RefreshResults(SessionManager session)
        {
            var key = new StringBuilder("results:").Append(session.LastResultsTitle);
            foreach (var entry in session.LastResults) key.Append(entry.ClientId).Append(entry.Place).Append(entry.FishAwarded).Append(',');
            SetModal(key.ToString(), () => BuildResults(session));

            if (_modalTimer != null)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(session.ResultsVisibleUntil - Time.unscaledTime));
                UIKit.SetText(_modalTimer, $"Во дворе — аукцион за зону. Через {left}…");
            }
        }

        private VisualElement BuildResults(SessionManager session)
        {
            var panel = UIKit.Box("panel");
            panel.Add(UIKit.Text("Итоги: " + session.LastResultsTitle, "h1"));

            foreach (var entry in session.LastResults)
            {
                var player = NetworkPlayer.FindByClientId(entry.ClientId);
                bool me = player == NetworkPlayer.Local;
                var row = UIKit.Layout("player-row");
                if (me) row.AddToClassList("player-row--me");
                row.Add(UIKit.Text($"{entry.Place}.", "h2"));
                row.Add(UIKit.Spacer(0));
                if (player != null)
                {
                    var swatch = UIKit.Swatch(player.Color);
                    swatch.style.marginLeft = 10;
                    row.Add(swatch);
                }
                row.Add(UIKit.Text((player != null ? player.DisplayName : "ушёл") + (me ? " (ты)" : ""), "text", "grow"));
                row.Add(UIKit.Fish($"+{entry.FishAwarded}"));
                var total = UIKit.Text($"всего {entry.TotalFish}", "muted");
                total.style.marginLeft = 14;
                row.Add(total);
                panel.Add(row);
            }

            panel.Add(UIKit.Spacer());
            _modalTimer = UIKit.Text("", "muted");
            panel.Add(_modalTimer);
            return panel;
        }

        // Финал партии.

        private float _finalSince = -1f;
        private int _shownNominations = -1;

        private void RefreshFinal(SessionManager session)
        {
            if (_finalSince < 0f) _finalSince = Time.unscaledTime;
            var standings = session.GetStandings();

            // Номинации открываются по одной: сначала король двора, потом каждые 1.3 с новая карточка.
            int total = session.Nominations.Count;
            int revealed = Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - _finalSince - 1.5f) / 1.3f) + 1, 0, total);
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            if (revealed != _shownNominations)
            {
                if (revealed > 0 && revealed > _shownNominations)
                {
                    Maoshi.Audio.AudioManager.Play("card_reveal");
                    if (session.Nominations[revealed - 1].ClientId == me) Maoshi.Audio.AudioManager.Play("fish_big");
                }
                _shownNominations = revealed;
            }

            // Банки: 3 за победу в партии + по 1 за номинацию. Начисляются, когда открыта последняя карточка.
            bool king = standings.Count > 0 && standings[0].OwnerClientId == me;
            int myNominations = 0;
            foreach (var n in session.Nominations) if (n.ClientId == me) myNominations++;
            int reward = (king ? 3 : 0) + myNominations;
            if (revealed >= total && Maoshi.Settings.Cosmetics.Award(session.FinalId.Value, reward))
                Maoshi.Audio.AudioManager.Play("win");

            var key = new StringBuilder("final:");
            foreach (var player in standings)
                key.Append(player.OwnerClientId).Append(':').Append(session.ZoneCount(player.OwnerClientId)).Append(':').Append(player.Fish.Value).Append(',');
            key.Append("n").Append(revealed).Append('/').Append(total);
            SetModal(key.ToString(), () => BuildFinal(session, standings, revealed, me));
        }

        /// <summary>«+N банок»: значок с банкой; animate — выскакивает с отскоком, а копия банки улетает вверх.</summary>
        private static VisualElement CanReward(int amount, bool animate)
        {
            var wrap = UIKit.Layout("can-reward");
            var badge = UIKit.Layout("can-reward__badge");
            badge.Add(UIKit.Text("+" + amount, "can-reward__text"));
            badge.Add(UIKit.CanImage(26f));
            wrap.Add(badge);
            if (!animate) return wrap;

            badge.AddToClassList("can-reward__badge--start");
            var fly = UIKit.CanImage(34f);
            fly.AddToClassList("can-fly");
            wrap.Add(fly);
            // Классы с переходами ставим кадром позже — тогда UI Toolkit анимирует изменение.
            badge.schedule.Execute(() =>
            {
                badge.RemoveFromClassList("can-reward__badge--start");
                fly.AddToClassList("can-fly--go");
            }).ExecuteLater(40);
            return wrap;
        }

        private static VisualElement BuildFinal(SessionManager session, List<NetworkPlayer> standings, int revealed, ulong localId)
        {
            var panel = UIKit.Box("panel", "panel--wide");
            string king = standings.Count > 0 ? standings[0].DisplayName : "?";
            panel.Add(UIKit.Text("король двора", "chalk"));
            var title = UIKit.Text(king + "!", "big-center", "big-center--title");
            panel.Add(title);
            if (standings.Count > 0 && standings[0].OwnerClientId == localId)
            {
                var kingReward = UIKit.Layout("final-king-reward");
                kingReward.Add(UIKit.Text("за победу", "muted"));
                kingReward.Add(CanReward(3, revealed == 0));
                panel.Add(kingReward);
            }
            panel.Add(UIKit.Spacer(8));

            for (int i = 0; i < standings.Count; i++)
            {
                var player = standings[i];
                bool me = player == NetworkPlayer.Local;
                var row = UIKit.Layout("player-row");
                if (me) row.AddToClassList("player-row--me");
                row.Add(UIKit.Text($"{i + 1}.", "h2"));
                var swatch = UIKit.Swatch(player.Color);
                swatch.style.marginLeft = 10;
                row.Add(swatch);
                row.Add(UIKit.Text(player.DisplayName + (me ? " (ты)" : ""), "text", "grow"));
                row.Add(UIKit.Text($"зон {session.ZoneCount(player.OwnerClientId)}", "muted"));
                var fishTotal = UIKit.Fish(player.Fish.Value, 22f, "text");
                fishTotal.style.marginLeft = 12;
                row.Add(fishTotal);
                panel.Add(row);
            }

            // Номинации партии.
            if (session.Nominations.Count > 0)
            {
                panel.Add(UIKit.Spacer(10));
                panel.Add(UIKit.Text("Номинации двора", "h2"));
                var grid = UIKit.Layout("nominations");
                for (int i = 0; i < session.Nominations.Count; i++)
                {
                    var nomination = session.Nominations[i];
                    var card = UIKit.Layout("nomination");
                    if (i >= revealed)
                    {
                        card.AddToClassList("nomination--hidden");
                        card.Add(UIKit.Text("?", "nomination__title"));
                        grid.Add(card);
                        continue;
                    }
                    if (i == revealed - 1) card.AddToClassList("nomination--new");
                    var player = NetworkPlayer.FindByClientId(nomination.ClientId);
                    bool me = player != null && player == NetworkPlayer.Local;
                    if (me) card.AddToClassList("nomination--me");
                    card.Add(UIKit.Text(nomination.Title.ToString(), "nomination__title"));
                    var who = UIKit.Layout("nomination__who");
                    if (player != null) who.Add(UIKit.Swatch(player.Color));
                    who.Add(UIKit.Text((player != null ? player.DisplayName : "ушёл") + (me ? " (ты)" : ""), "nomination__name"));
                    card.Add(who);
                    card.Add(UIKit.Text(nomination.Detail.ToString(), "nomination__detail"));
                    if (me) card.Add(CanReward(1, i == revealed - 1));
                    grid.Add(card);
                }
                panel.Add(grid);
            }

            // Сколько банок у тебя (сохраняются между партиями) и сколько получено за эту.
            int myNominations = 0;
            foreach (var n in session.Nominations) if (n.ClientId == localId) myNominations++;
            int reward = (standings.Count > 0 && standings[0].OwnerClientId == localId ? 3 : 0) + myNominations;
            var cansRow = UIKit.Layout("final-cans");
            cansRow.Add(UIKit.Text("Твои консервные банки:", "text"));
            cansRow.Add(UIKit.Cans(Maoshi.Settings.Cosmetics.Cans, 28f, "final-cans__value"));
            if (reward > 0) cansRow.Add(UIKit.Text(revealed >= session.Nominations.Count ? $"(+{reward} за партию)" : "…", "muted"));
            cansRow.Add(UIKit.Text("— трать в магазине в главном меню", "muted"));
            panel.Add(UIKit.Spacer(6));
            panel.Add(cansRow);

            panel.Add(UIKit.Spacer());
            var buttons = UIKit.Layout("final-buttons");
            if (NetworkManager.Singleton.IsServer)
                buttons.Add(UIKit.Button("Ещё партию", session.RestartSession, "btn--primary"));
            else
                buttons.Add(UIKit.Text("Хост может начать ещё партию.", "muted"));
            buttons.Add(UIKit.Button("Выйти в меню", () => ConnectionManager.Instance?.Leave(), "btn--ghost"));
            panel.Add(buttons);
            return panel;
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        private static string NameOf(ulong clientId)
        {
            var player = NetworkPlayer.FindByClientId(clientId);
            return player != null ? player.DisplayName : "ушёл";
        }
    }
}
