using System;
using System.Text;
using Maoshi.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Главное меню по макету «двор-стикер»: слева крупно свой кот (карточка на скотче) и выбор окраски,
    /// справа форма: имя, режим, «Создать игру», вход к другу, настройки и выход. Фон — вечерний двор с мелом.
    /// </summary>
    public class MainMenuScreen
    {
        public VisualElement Root { get; }

        private readonly TextField _name;
        private readonly TextField _address;
        private readonly Label _addressLabel;
        private readonly Button _localTab;
        private readonly Button _onlineTab;
        private readonly Label _error;
        private readonly VisualElement _previewImage;
        private readonly Label _skinName;
        private readonly Label _skinCount;
        private readonly Button[] _skinDots;

        private readonly VisualElement _manualBox;
        private readonly VisualElement _codeBox;
        private readonly VisualElement _lanBox;
        private readonly VisualElement _lanList;
        private readonly Label _lanHint;
        private readonly Button _manualToggle;
        private readonly Label _divider;
        private bool _manualOpen;
        private string _lanKey = "";

        private ConnectionMode _mode = ConnectionMode.Online;
        private string _addressText = "127.0.0.1";
        private string _codeText = "";

        private readonly VisualElement _cansBox;
        private int _shownCans = -1;

        public MainMenuScreen(Action openSettings, Action openShop = null)
        {
            Root = UIKit.Layout("layer", "menu-backdrop");
            UIKit.Backdrop(Root);

            var content = UIKit.Layout("layer", "menu-root");
            Root.Add(content);

            var logo = UIKit.Layout("logo-row");
            logo.Add(UIKit.Text("Maoshi", "title"));
            logo.Add(UIKit.Text("битва за двор", "subtitle", "chalk"));
            content.Add(logo);

            var columns = UIKit.Layout("menu-columns");
            content.Add(columns);

            // ── Свой кот ──
            var wrap = UIKit.Layout("skin-card-wrap");
            var card = UIKit.Box("panel", "skin-card");
            _previewImage = UIKit.Box("skin-preview-image");
            _previewImage.Add(UIKit.Text("это ты", "skin-preview-note", "chalk"));
            card.Add(_previewImage);

            var skinRow = UIKit.Row("skin-row");
            skinRow.Add(UIKit.Button("‹", () => StepSkin(-1), "skin-arrow"));
            var nameBox = UIKit.Layout("skin-name-box");
            _skinName = UIKit.Text("", "skin-name");
            _skinCount = UIKit.Text("", "skin-count");
            nameBox.Add(_skinName);
            nameBox.Add(_skinCount);
            skinRow.Add(nameBox);
            skinRow.Add(UIKit.Button("›", () => StepSkin(1), "skin-arrow"));
            card.Add(skinRow);

            var dots = UIKit.Layout("skin-dots");
            _skinDots = new Button[Maoshi.Cat.CatSkins.Count];
            for (int i = 0; i < _skinDots.Length; i++)
            {
                int index = i;
                var dot = new Button(() => SetSkin(index)) { tooltip = Maoshi.Cat.CatSkins.All[i].Name };
                dot.AddToClassList("skin-dot");
                dot.style.backgroundColor = Maoshi.Cat.CatSkins.All[i].Swatch;
                _skinDots[i] = dot;
                dots.Add(dot);
            }
            card.Add(dots);

            // Магазин аксессуаров и банки.
            var shopRow = UIKit.Row("shop-row");
            _cansBox = UIKit.Layout("shop-row__cans");
            shopRow.Add(_cansBox);
            var shop = UIKit.Button("Магазин", () => openShop?.Invoke(), "btn--secondary", "grow");
            shop.style.marginLeft = 10;
            shopRow.Add(shop);
            card.Add(shopRow);
            wrap.Add(card);
            wrap.Add(UIKit.Picture("Tape", "tape"));
            columns.Add(wrap);

            // ── Форма ──
            var panel = UIKit.Box("panel", "menu-form");

            bool steam = SteamService.Ready;
            panel.Add(UIKit.Text(steam ? "Твой ник в Steam" : "Как тебя зовут во дворе", "label-small-caps"));
            string steamName = steam ? ConnectionManager.SanitizeName(SteamService.PlayerName) : null;
            _name = new TextField { maxLength = ConnectionManager.MaxNameLength, value = steamName ?? "Кот" + UnityEngine.Random.Range(100, 1000) };
            _name.AddToClassList("field");
            // Со Steam имя берётся из Steam — поле только показывает его.
            if (steam) _name.SetEnabled(false);
            panel.Add(_name);

            // Вкладки: онлайн по коду (основная) и игра в локальной сети с автопоиском.
            var segmented = UIKit.Box("segmented");
            segmented.style.marginTop = 14;
            _onlineTab = new Button(() => SetMode(ConnectionMode.Online)) { text = steam ? "Онлайн (Steam)" : "Онлайн по коду" };
            _localTab = new Button(() => SetMode(ConnectionMode.Local)) { text = "По локальной сети" };
            _onlineTab.AddToClassList("segment");
            _localTab.AddToClassList("segment");
            segmented.Add(_onlineTab);
            segmented.Add(_localTab);
            panel.Add(segmented);

            var create = UIKit.Button("Создать игру", Create, "btn--primary");
            create.style.marginTop = 18;
            panel.Add(create);

            var divider = UIKit.Layout("divider");
            divider.Add(UIKit.Layout("divider__line"));
            _divider = UIKit.Text("или зайди к другу", "divider__label", "chalk");
            divider.Add(_divider);
            divider.Add(UIKit.Layout("divider__line"));
            panel.Add(divider);

            // ── Локальная сеть: найденные игры ──
            _lanBox = UIKit.Layout("lan-box");
            _lanList = UIKit.Layout("lan-list");
            _lanBox.Add(_lanList);
            _lanHint = UIKit.Text("", "muted", "lan-hint");
            _lanBox.Add(_lanHint);
            _manualToggle = UIKit.Button("Ввести IP вручную", () => { _manualOpen = !_manualOpen; UpdateModeVisuals(); }, "btn--small", "btn--ghost", "lan-manual-toggle");
            _lanBox.Add(_manualToggle);
            panel.Add(_lanBox);

            // ── Поле ввода: код (онлайн) или IP (вручную в локальной сети) ──
            _codeBox = UIKit.Layout("code-box");
            var joinRow = UIKit.Row();
            _address = new TextField { maxLength = 64 };
            _address.AddToClassList("field");
            _address.AddToClassList("grow");
            _address.RegisterValueChangedCallback(e =>
            {
                if (_mode == ConnectionMode.Local) _addressText = e.newValue;
                else _codeText = e.newValue.ToUpperInvariant();
            });
            joinRow.Add(_address);
            var join = UIKit.Button("Войти", Join, "btn--secondary");
            join.style.marginLeft = 10;
            join.style.marginTop = 0;
            join.style.height = 60;
            join.style.width = 132;
            joinRow.Add(join);
            _codeBox.Add(joinRow);
            _addressLabel = UIKit.Text("", "muted");
            _addressLabel.style.marginTop = 6;
            _codeBox.Add(_addressLabel);
            _manualBox = _codeBox;
            panel.Add(_codeBox);

            _error = UIKit.Text("", "error");
            panel.Add(_error);

            var bottom = UIKit.Row();
            bottom.style.marginTop = 14;
            var settings = UIKit.Button("Настройки", openSettings, "btn--ghost", "grow");
            settings.style.marginRight = 10;
            bottom.Add(settings);
            bottom.Add(UIKit.Button("Выйти из игры", Quit, "btn--ghost", "grow"));
            panel.Add(bottom);

            columns.Add(panel);
            Root.Add(UIKit.Text("версия " + Application.version, "version"));

            SetMode(ConnectionMode.Online);
            UpdateSkinVisuals();
        }

        private void StepSkin(int direction)
        {
            // −1 («Случайная» — по цвету игрока) и дальше все окраски по кругу.
            int count = Maoshi.Cat.CatSkins.Count + 1;
            int value = Maoshi.Settings.GameSettings.CatSkin + 1;
            value = ((value + direction) % count + count) % count;
            SetSkin(value - 1);
        }

        private void SetSkin(int skin)
        {
            Maoshi.Settings.GameSettings.CatSkin = skin;
            Maoshi.Settings.GameSettings.Save();
            UpdateSkinVisuals();
        }

        private void UpdateSkinVisuals()
        {
            int skin = Maoshi.Settings.GameSettings.CatSkin;
            UIKit.SetText(_skinName, Maoshi.Cat.CatSkins.NameOf(skin));
            UIKit.SetText(_skinCount, skin >= 0 ? $"{skin + 1} из {Maoshi.Cat.CatSkins.Count} окрасов" : "окрас по цвету игрока");
            for (int i = 0; i < _skinDots.Length; i++) _skinDots[i].EnableInClassList("skin-dot--active", i == skin);
        }

        private void UpdatePreview()
        {
            var preview = CatPreview.Get();
            if (preview == null) return;
            preview.Touch();
            int skin = Maoshi.Settings.GameSettings.CatSkin;
            preview.SetSkin(skin >= 0 ? skin : 0);
            preview.FocusHead(false);
            preview.SetAccessories(Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Head),
                Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Neck),
                Maoshi.Settings.Cosmetics.Equipped(Maoshi.Cat.AccessorySlot.Face));
            if (_shownCans != Maoshi.Settings.Cosmetics.Cans)
            {
                _shownCans = Maoshi.Settings.Cosmetics.Cans;
                _cansBox.Clear();
                _cansBox.Add(UIKit.Cans(_shownCans, 30f, "shop-row__value"));
            }
            if (_previewImage.style.backgroundImage.value.renderTexture != preview.Texture)
                _previewImage.style.backgroundImage = Background.FromRenderTexture(preview.Texture);
        }

        public void Refresh(ConnectionManager connection)
        {
            string error = connection != null ? connection.LastError : "";
            UIKit.SetText(_error, error ?? "");
            UIKit.Show(_error, !string.IsNullOrEmpty(error));
            UpdatePreview();
            if (_mode == ConnectionMode.Local) RefreshLan();
        }

        /// <summary>Список игр в локальной сети (перестраивается, только когда что-то поменялось).</summary>
        private void RefreshLan()
        {
            var discovery = LanDiscovery.Instance;
            if (discovery == null) return;
            if (!discovery.IsListening) discovery.StartListening();

            var games = discovery.Games;
            var key = new StringBuilder();
            foreach (var game in games)
                key.Append(game.Key).Append(game.Address).Append(game.HostName).Append(game.Players).Append(game.Started).Append('|');
            if (key.ToString() == _lanKey) return;
            _lanKey = key.ToString();

            _lanList.Clear();
            if (games.Count == 0)
            {
                var empty = UIKit.Box("lan-empty");
                empty.Add(UIKit.Text("Ищем игры в твоей сети…", "chalk", "lan-empty__title"));
                empty.Add(UIKit.Text("Друг жмёт «Создать игру» на этой же вкладке — игра появится здесь сама", "muted", "lan-empty__text"));
                _lanList.Add(empty);
                return;
            }

            foreach (var game in games)
            {
                var row = UIKit.Box("player-row", "lan-row");
                var info = UIKit.Layout("grow");
                info.Add(UIKit.Text(game.HostName, "text", "lan-row__name"));
                info.Add(UIKit.Text($"{game.Players} из {game.MaxPlayers} · {game.Address}", "muted", "lan-row__meta"));
                row.Add(info);
                if (game.Started || game.Players >= game.MaxPlayers)
                {
                    row.Add(UIKit.Text(game.Started ? "идёт игра" : "мест нет", "tag", "tag--waiting"));
                }
                else
                {
                    string target = game.Address + ":" + game.Port;
                    var button = UIKit.Button("Войти", () => JoinLan(target), "btn--small", "btn--secondary", "lan-row__join");
                    row.Add(button);
                }
                _lanList.Add(row);
            }
        }

        private void SetMode(ConnectionMode mode)
        {
            _mode = mode;
            _localTab.EnableInClassList("segment--active", mode == ConnectionMode.Local);
            _onlineTab.EnableInClassList("segment--active", mode == ConnectionMode.Online);
            _address.SetValueWithoutNotify(mode == ConnectionMode.Local ? _addressText : _codeText);

            var discovery = LanDiscovery.Instance;
            if (discovery != null)
            {
                if (mode == ConnectionMode.Local) discovery.StartListening();
                else discovery.StopListening();
            }
            _lanKey = "-";
            UpdateModeVisuals();
            if (mode == ConnectionMode.Local) RefreshLan();
        }

        private void UpdateModeVisuals()
        {
            bool local = _mode == ConnectionMode.Local;
            UIKit.Show(_lanBox, local);
            UIKit.Show(_manualBox, !local || _manualOpen);
            UIKit.SetText(_divider, local ? "или зайди к другу в сети" : "или зайди к другу по коду");
            UIKit.SetText(_manualToggle, _manualOpen ? "Скрыть ввод IP" : "Ввести IP вручную");

            int port = ConnectionManager.Instance != null ? ConnectionManager.Instance.LocalPort : 7777;
            if (local)
            {
                string mine = LanDiscovery.LocalAddresses();
                UIKit.SetText(_addressLabel, string.IsNullOrEmpty(mine) ? $"IP хоста, порт {port}" : $"IP хоста, порт {port}. Твой IP: {mine}");
            }
            else UIKit.SetText(_addressLabel, SteamService.Ready
                ? "Код игры от друга (6 символов) — или просто прими приглашение в Steam"
                : $"Код игры от друга (6 символов). {SteamService.Problem} — онлайн через Unity");
            UIKit.SetText(_lanHint, "");
            UIKit.Show(_lanHint, false);
        }

        private void Create()
        {
            LanDiscovery.Instance?.StopListening();
            ConnectionManager.Instance?.HostGame(_mode, _name.value);
        }

        private void Join()
        {
            string target = _mode == ConnectionMode.Local ? _addressText : _codeText;
            LanDiscovery.Instance?.StopListening();
            ConnectionManager.Instance?.JoinGame(_mode, _name.value, target);
        }

        private void JoinLan(string target)
        {
            LanDiscovery.Instance?.StopListening();
            ConnectionManager.Instance?.JoinGame(ConnectionMode.Local, _name.value, target);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>«Подключение…» с кнопкой отмены.</summary>
    public class ConnectingScreen
    {
        public VisualElement Root { get; }
        private readonly Label _title;
        private readonly Label _mode;

        public ConnectingScreen()
        {
            Root = UIKit.Layout("layer", "center", "menu-backdrop");
            UIKit.Backdrop(Root);
            var panel = UIKit.Box("panel", "panel--narrow");
            _title = UIKit.Text("Подключение", "h1");
            _mode = UIKit.Text("", "muted");
            panel.Add(_title);
            panel.Add(_mode);
            panel.Add(UIKit.Spacer());
            panel.Add(UIKit.Button("Отмена", () => ConnectionManager.Instance?.Leave(), "btn--ghost"));
            Root.Add(panel);
        }

        public void Refresh(ConnectionManager connection)
        {
            string dots = new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
            UIKit.SetText(_title, "Подключение" + dots);
            UIKit.SetText(_mode, connection.Mode != ConnectionMode.Online ? "Локальная сеть"
                : SteamService.Ready ? "Онлайн через Steam" : "Онлайн через Unity Cloud");
        }
    }

    /// <summary>
    /// Лобби: код сессии, список игроков с готовностью, старт (хост) или «Готов» (клиент).
    /// Со Steam справа — друзья в сети с кнопкой «Позвать» (приглашение Steam + сообщение с кодом в чат).
    /// </summary>
    public class LobbyScreen
    {
        public VisualElement Root { get; }

        private readonly VisualElement _panel;
        private readonly Label _info;
        private readonly Button _copy;
        private readonly Label _count;
        private readonly VisualElement _players;
        private readonly Button _main;
        private readonly Label _reason;
        private readonly VisualElement _friendsColumn;
        private readonly ScrollView _friendsList;
        private readonly Label _friendsHint;
        private string _playersKey = "";
        private string _friendsKey = "";
        private float _nextFriendsRefresh;
        private float _copiedUntil;
        private readonly System.Collections.Generic.HashSet<ulong> _invited = new();
        private bool _avatarsDirty;

        public LobbyScreen()
        {
            Root = UIKit.Layout("layer", "center", "menu-backdrop");
            UIKit.Backdrop(Root);
            _panel = UIKit.Box("panel", "lobby-panel");

            var columns = UIKit.Row("lobby-columns");
            _panel.Add(columns);

            // ── Слева: код, игроки, кнопки ──
            var left = UIKit.Layout("lobby-main");
            columns.Add(left);
            left.Add(UIKit.Text("Лобби", "h1"));

            var infoRow = UIKit.Row("row--spread", "lobby-code-row");
            _info = UIKit.Text("", "text", "lobby-code");
            // Код тоже копируется по клику.
            _info.RegisterCallback<ClickEvent>(_ => CopyCode());
            _copy = UIKit.Button("Копировать", CopyCode, "btn--small", "btn--ghost", "lobby-copy");
            _copy.style.marginTop = 0;
            infoRow.Add(_info);
            infoRow.Add(_copy);
            left.Add(infoRow);

            _count = UIKit.Text("", "label-small-caps");
            left.Add(_count);
            _players = new VisualElement();
            _players.style.minHeight = 200;
            left.Add(_players);

            left.Add(UIKit.Spacer());
            _main = UIKit.Button("", OnMain, "btn--primary");
            left.Add(_main);
            _reason = UIKit.Text("", "muted");
            _reason.style.marginTop = 6;
            left.Add(_reason);
            left.Add(UIKit.Button("Выйти", () => ConnectionManager.Instance?.Leave(), "btn--ghost"));

            // ── Справа: друзья Steam ──
            _friendsColumn = UIKit.Layout("lobby-friends");
            _friendsColumn.Add(UIKit.Text("Позвать друзей", "h2", "lobby-friends__title"));
            _friendsHint = UIKit.Text("Друзья в сети. Им придёт приглашение Steam и сообщение с кодом.", "muted", "lobby-friends__hint");
            _friendsColumn.Add(_friendsHint);
            _friendsList = new ScrollView(ScrollViewMode.Vertical);
            _friendsList.AddToClassList("lobby-friends__list");
            _friendsList.AddToClassList("settings-scroll");
            _friendsColumn.Add(_friendsList);
            _friendsColumn.Add(UIKit.Button("Окно друзей Steam", () => ConnectionManager.Instance?.InviteFriends(), "btn--small", "btn--ghost", "lobby-friends__steam"));
            columns.Add(_friendsColumn);

            Root.Add(_panel);
        }

        private void CopyCode()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null || string.IsNullOrEmpty(connection.SessionCode)) return;
            GUIUtility.systemCopyBuffer = connection.SessionCode;
            _copiedUntil = Time.unscaledTime + 1.6f;
            Debug.Log($"[Maoshi] Код {connection.SessionCode} скопирован (в буфере: «{GUIUtility.systemCopyBuffer}»)");
        }

        public void Refresh(ConnectionManager connection)
        {
            bool online = connection.Mode == ConnectionMode.Online && !string.IsNullOrEmpty(connection.SessionCode);
            UIKit.SetText(_info, online ? $"Код: <size=30><b>{connection.SessionCode}</b></size>\n<size=15>{(connection.UsingSteam ? "или пригласи через Steam" : "отправь друзьям")}</size>"
                : "Локальная игра — друзья в твоей сети увидят её во вкладке «По локальной сети»");
            UIKit.Show(_copy, online);
            UIKit.SetText(_copy, Time.unscaledTime < _copiedUntil ? "Скопировано ✓" : "Копировать");

            bool friends = connection.CanInviteFriends;
            UIKit.Show(_friendsColumn, friends);
            _panel.EnableInClassList("lobby-panel--wide", friends);
            if (friends) RefreshFriends();
            else _invited.Clear();

            var players = NetworkPlayer.All;
            UIKit.SetText(_count, $"Игроки: {players.Count} из {ConnectionManager.MaxPlayers}");
            RebuildPlayersIfChanged(players);

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (isHost)
            {
                bool canStart = connection.CanStartGame(out string reason);
                UIKit.SetText(_main, "Начать игру");
                _main.SetEnabled(canStart);
                UIKit.SetText(_reason, canStart ? "" : reason);
            }
            else
            {
                var local = NetworkPlayer.Local;
                bool ready = local != null && local.IsReady.Value;
                UIKit.SetText(_main, ready ? "Не готов" : "Готов!");
                _main.SetEnabled(local != null);
                UIKit.SetText(_reason, ready ? "Ждём, пока хост начнёт игру…" : "Нажми «Готов!», когда будешь готов");
            }

            UIKit.Show(_reason, !string.IsNullOrEmpty(_reason.text));
        }

        /// <summary>Список друзей Steam раз в 3 с (или сразу, когда догрузилась аватарка).</summary>
        private void RefreshFriends()
        {
            if (!_avatarsDirty && Time.unscaledTime < _nextFriendsRefresh) return;
            _nextFriendsRefresh = Time.unscaledTime + 3f;

            var friends = SteamService.OnlineFriends();
            var inLobby = new System.Collections.Generic.HashSet<string>();
            foreach (var player in NetworkPlayer.All) inLobby.Add(player.DisplayName);

            var key = new StringBuilder();
            foreach (var f in friends) key.Append(f.Id).Append((int)f.Status).Append(_invited.Contains(f.Id)).Append(inLobby.Contains(ConnectionManager.SanitizeName(f.Name))).Append('|');
            if (!_avatarsDirty && key.ToString() == _friendsKey) return;
            _friendsKey = key.ToString();
            _avatarsDirty = false;

            _friendsList.Clear();
            UIKit.SetText(_friendsHint, friends.Count == 0
                ? "Сейчас никого из друзей нет в сети. Отправь им код комнаты."
                : "Друзья в сети. Им придёт приглашение Steam и сообщение с кодом.");

            foreach (var friend in friends)
            {
                var row = UIKit.Box("friend-row");
                var avatar = UIKit.Layout("friend-row__avatar");
                var texture = SteamService.Avatar(friend.Id, () => _avatarsDirty = true);
                if (texture != null) avatar.style.backgroundImage = new StyleBackground(texture);
                row.Add(avatar);

                var text = UIKit.Layout("friend-row__text");
                text.Add(UIKit.Text(friend.Name, "friend-row__name"));
                text.Add(UIKit.Text(friend.Status switch
                {
                    SteamService.FriendStatus.InThisGame => "уже в Maoshi",
                    SteamService.FriendStatus.Away => "отошёл",
                    _ => "в сети"
                }, "friend-row__status", friend.Status == SteamService.FriendStatus.InThisGame ? "friend-row__status--game" : "friend-row__status--online"));
                row.Add(text);

                bool here = inLobby.Contains(ConnectionManager.SanitizeName(friend.Name));
                if (here)
                {
                    row.Add(UIKit.Text("в лобби", "tag", "tag--ready"));
                }
                else if (_invited.Contains(friend.Id))
                {
                    row.Add(UIKit.Button("Позван ✓", () => Invite(friend.Id), "btn--small", "btn--ghost", "friend-row__button"));
                }
                else
                {
                    row.Add(UIKit.Button("Позвать", () => Invite(friend.Id), "btn--small", "btn--secondary", "friend-row__button"));
                }
                _friendsList.Add(row);
            }
        }

        private void Invite(ulong friendId)
        {
            var connection = ConnectionManager.Instance;
            if (connection == null) return;
            if (connection.InviteFriend(friendId)) _invited.Add(friendId);
            _nextFriendsRefresh = 0f;
        }

        private void OnMain()
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (isHost) ConnectionManager.Instance?.StartGame();
            else NetworkPlayer.Local?.ToggleReady();
        }

        private void RebuildPlayersIfChanged(System.Collections.Generic.IReadOnlyList<NetworkPlayer> players)
        {
            var key = new StringBuilder();
            foreach (var player in players)
                key.Append(player.OwnerClientId).Append(player.DisplayName).Append(player.ColorIndex.Value).Append(player.IsReady.Value).Append('|');
            if (key.ToString() == _playersKey) return;
            _playersKey = key.ToString();

            _players.Clear();
            foreach (var player in players)
            {
                bool me = player == NetworkPlayer.Local;
                var row = UIKit.Box("player-row");
                if (me) row.AddToClassList("player-row--me");
                row.Add(UIKit.Swatch(player.Color));
                row.Add(UIKit.Text(player.DisplayName + (me ? "  (ты)" : ""), "text", "grow"));
                if (player.IsHostPlayer) row.Add(UIKit.Text("хост", "tag"));
                else row.Add(UIKit.Text(player.IsReady.Value ? "готов" : "ждём", "tag", player.IsReady.Value ? "tag--ready" : "tag--waiting"));
                _players.Add(row);
            }
        }
    }
}
