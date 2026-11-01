using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.Network
{
    /// <summary>Способ подключения к игре.</summary>
    public enum ConnectionMode
    {
        /// <summary>Напрямую по IP: один компьютер или локальная сеть. Не требует Unity Cloud.</summary>
        Local = 0,
        /// <summary>
        /// Через интернет: сеть Steam (лобби Steam + код, приглашения друзей), а если Steam не запущен —
        /// Unity Multiplayer Services (Relay + код сессии).
        /// </summary>
        Online = 1
    }

    /// <summary>Состояние подключения локального игрока.</summary>
    public enum ConnectionState
    {
        /// <summary>В главном меню, ни к чему не подключены.</summary>
        Offline,
        /// <summary>Идёт создание игры или вход в неё.</summary>
        Connecting,
        /// <summary>Подключены: в лобби или уже в игре.</summary>
        Connected
    }

    /// <summary>
    /// Точка входа в сеть: создание игры (хост), вход (клиент), выход и проверка подключений.
    /// Живёт всю сессию (DontDestroyOnLoad) на одном объекте с NetworkManager в сцене Bootstrap.
    /// Хост — он же сервер: всё, что связано с правилами, решает он.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ConnectionManager : MonoBehaviour
    {
        public const int MaxPlayers = 8;
        public const int MinPlayersToStart = 2;
        public const int MaxNameLength = 16;

        public static ConnectionManager Instance { get; private set; }

        [Header("Сцены")]
        [SerializeField] private string mainMenuScene = "MainMenu";
        [SerializeField] private string hubScene = "Hub";

        [Header("Сессия")]
        [Tooltip("Префаб с SessionManager. Сервер создаёт его при старте игры.")]
        [SerializeField] private NetworkObject sessionManagerPrefab;

        [Header("Локальное подключение")]
        [SerializeField] private ushort localPort = 7777;

        [Header("Отладка")]
        [Tooltip("Только в редакторе: можно начать игру одному, чтобы быстро проверить сцену.")]
        [SerializeField] private bool allowSoloStartInEditor = true;

        public ConnectionState State { get; private set; } = ConnectionState.Offline;
        public ConnectionMode Mode { get; private set; }

        /// <summary>Код онлайн-сессии, который хост диктует друзьям. Пусто в локальном режиме.</summary>
        public string SessionCode { get; private set; }

        /// <summary>Текст последней ошибки для показа в меню.</summary>
        public string LastError { get; private set; }

        public ushort LocalPort => localPort;

        /// <summary>Мы сейчас в сцене главного меню (там же показывается лобби).</summary>
        public bool IsInMenu => SceneManager.GetActiveScene().name == mainMenuScene;

        /// <summary>Только на сервере: игра запущена, новых игроков больше не пускаем.</summary>
        public bool GameStarted { get; private set; }

        /// <summary>Вызывается при любом изменении состояния подключения или сцены.</summary>
        public event Action StateChanged;

        private ISession _session;
        private Steamworks.Data.Lobby? _lobby;
        private SteamNetTransport _steamTransport;
        private UnityTransport _unityTransport;

        /// <summary>Онлайн-игра идёт через Steam (лобби Steam): можно приглашать друзей.</summary>
        public bool UsingSteam => _lobby.HasValue;

        /// <summary>Можно открыть окно Steam «Пригласить друзей».</summary>
        public bool CanInviteFriends => _lobby.HasValue && SteamService.Ready && !GameStarted;
        private bool _servicesReady;
        private string _localName = "Кот";

        // Имена, присланные клиентами при подключении. NetworkPlayer забирает своё имя при спавне.
        private readonly Dictionary<ulong, string> _pendingNames = new();

        private static NetworkManager Net => NetworkManager.Singleton;
        private UnityTransport Transport => _unityTransport != null ? _unityTransport : (_unityTransport = Net.GetComponent<UnityTransport>());

        /// <summary>Выбрать транспорт Netcode перед подключением: Steam или UnityTransport (локальная сеть, Unity Relay).</summary>
        private void UseTransport(NetworkTransport transport)
        {
            if (Net.NetworkConfig.NetworkTransport != transport) Net.NetworkConfig.NetworkTransport = transport;
        }

        // ───────────────────────── Жизненный цикл ─────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (GetComponent<LanDiscovery>() == null) gameObject.AddComponent<LanDiscovery>();
        }

        private void Start()
        {
            Net.ConnectionApprovalCallback = ApprovalCheck;
            Net.OnClientConnectedCallback += OnClientConnected;
            Net.OnClientDisconnectCallback += OnClientDisconnect;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            // Bootstrap — служебная сцена: сразу уходим в главное меню.
            if (SceneManager.GetActiveScene().name != mainMenuScene)
                SceneManager.LoadScene(mainMenuScene);

            _unityTransport = Net.GetComponent<UnityTransport>();
            _steamTransport = GetComponent<SteamNetTransport>();
            if (_steamTransport == null) _steamTransport = gameObject.AddComponent<SteamNetTransport>();
            UseTransport(_unityTransport);

            // Приглашения Steam: принял приглашение друга / «Присоединиться» в списке друзей.
            SteamService.JoinLobbyRequested += OnSteamJoinRequested;
            ulong launchLobby = SteamService.LaunchLobby();
            if (launchLobby != 0) StartCoroutine(JoinLaunchLobby(launchLobby));

            StartCoroutine(CommandLineAutoConnect());
        }

        /// <summary>Игру запустили из приглашения Steam («+connect_lobby id»): ждём меню и заходим.</summary>
        private System.Collections.IEnumerator JoinLaunchLobby(ulong lobbyId)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            JoinSteamLobby(lobbyId, SteamService.PlayerName);
        }

        private async void OnSteamJoinRequested(ulong lobbyId)
        {
            if (_lobby.HasValue && _lobby.Value.Id.Value == lobbyId) return; // уже там
            // Уже в другой игре — выходим из неё и идём к другу.
            if (State != ConnectionState.Offline) await CleanupAsync();
            JoinSteamLobby(lobbyId, SteamService.PlayerName);
        }

        /// <summary>
        /// Для проверки сети тестовой копией (Maoshi → Test): параметры запуска
        /// -maoshiHost online|local, -maoshiJoin КОД, -maoshiJoinLan (первая найденная игра в сети), -maoshiName Имя.
        /// </summary>
        private System.Collections.IEnumerator CommandLineAutoConnect()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] == key) return i + 1 < args.Length ? args[i + 1] : "";
                return null;
            }

            string host = Arg("-maoshiHost"), join = Arg("-maoshiJoin"), joinLan = Arg("-maoshiJoinLan");
            if (host == null && join == null && joinLan == null) yield break;
            string name = Arg("-maoshiName") ?? "Тестовый кот";
            yield return new WaitForSecondsRealtime(2f);

            if (host != null) HostGame(host == "local" ? ConnectionMode.Local : ConnectionMode.Online, name);
            else if (join != null) JoinGame(ConnectionMode.Online, name, join);
            else
            {
                var discovery = LanDiscovery.Instance;
                discovery.StartListening();
                float until = Time.realtimeSinceStartup + 15f;
                while (discovery.Games.Count == 0 && Time.realtimeSinceStartup < until) yield return null;
                if (discovery.Games.Count == 0) { Debug.LogWarning("[Maoshi] Игр в сети не найдено"); yield break; }
                var game = discovery.Games[0];
                discovery.StopListening();
                JoinGame(ConnectionMode.Local, name, game.Address + ":" + game.Port);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SteamService.JoinLobbyRequested -= OnSteamJoinRequested;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            }

            Instance = null;
        }

        // ───────────────────────── Публичные команды (вызывает UI) ─────────────────────────

        /// <summary>Создать игру и стать хостом.</summary>
        public async void HostGame(ConnectionMode mode, string playerName)
        {
            if (State != ConnectionState.Offline) return;
            BeginConnecting(mode, playerName);

            try
            {
                if (mode == ConnectionMode.Online && SteamService.Ready)
                {
                    await HostSteamAsync();
                }
                else if (mode == ConnectionMode.Local)
                {
                    UseTransport(Transport);
                    // Слушаем все сетевые интерфейсы, чтобы могли зайти и с другого ПК в локальной сети.
                    Transport.SetConnectionData("127.0.0.1", localPort, "0.0.0.0");
                    if (!Net.StartHost())
                        throw new Exception($"не удалось запустить хост. Возможно, порт {localPort} занят.");
                    // Маячок: другие компьютеры в сети увидят игру в списке без ввода IP.
                    var discovery = LanDiscovery.Instance;
                    if (discovery != null)
                        discovery.BeaconSource = () => (_localName, Net != null && Net.IsServer ? Net.ConnectedClientsIds.Count : 0,
                            MaxPlayers, localPort, GameStarted);
                }
                else
                {
                    UseTransport(Transport);
                    await EnsureServicesAsync();

                    // Сессия сама создаёт Relay-аллокацию и запускает NetworkManager как хост.
                    var options = new SessionOptions
                    {
                        MaxPlayers = MaxPlayers,
                        IsPrivate = true // вход только по коду, в публичном списке не видна
                    }.WithRelayNetwork();

                    _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                    SessionCode = _session.Code;
                    Debug.Log($"[Maoshi] Онлайн-игра создана, код {SessionCode}");
                    NotifyChanged();
                }
                // State станет Connected в OnClientConnected, когда хост подключится сам к себе.
            }
            catch (Exception e)
            {
                await FailAsync("Не удалось создать игру: " + e.Message);
            }
        }

        /// <summary>Войти в чужую игру: по IP (локально) или по коду сессии (онлайн).</summary>
        public async void JoinGame(ConnectionMode mode, string playerName, string addressOrCode)
        {
            if (State != ConnectionState.Offline) return;
            BeginConnecting(mode, playerName);

            try
            {
                if (mode == ConnectionMode.Online && SteamService.Ready)
                {
                    if (string.IsNullOrWhiteSpace(addressOrCode))
                        throw new Exception("введи код игры.");
                    await JoinSteamByCodeAsync(addressOrCode.Trim().ToUpperInvariant());
                }
                else if (mode == ConnectionMode.Local)
                {
                    UseTransport(Transport);
                    // Адрес «IP» или «IP:порт» (из списка найденных игр приходит с портом).
                    string address = string.IsNullOrWhiteSpace(addressOrCode) ? "127.0.0.1" : addressOrCode.Trim();
                    ushort port = localPort;
                    int colon = address.LastIndexOf(':');
                    if (colon > 0 && ushort.TryParse(address.Substring(colon + 1), out ushort parsed))
                    {
                        port = parsed;
                        address = address.Substring(0, colon);
                    }
                    Transport.SetConnectionData(address, port);
                    if (!Net.StartClient())
                        throw new Exception("не удалось запустить клиент.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(addressOrCode))
                        throw new Exception("введи код сессии.");

                    UseTransport(Transport);
                    await EnsureServicesAsync();

                    // Сессия сама подключается к Relay хоста и запускает NetworkManager как клиент.
                    _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(addressOrCode.Trim().ToUpperInvariant());
                    SessionCode = _session.Code;
                    Debug.Log($"[Maoshi] Вошли в онлайн-игру по коду {SessionCode}");
                    NotifyChanged();
                }
                // State станет Connected в OnClientConnected. Если хост недоступен — придёт OnClientDisconnect.
            }
            catch (Exception e)
            {
                await FailAsync("Не удалось войти в игру: " + e.Message);
            }
        }

        /// <summary>Выйти из игры или отменить подключение и вернуться в главное меню.</summary>
        public async void Leave()
        {
            LastError = null;
            await CleanupAsync();
        }

        /// <summary>Можно ли хосту начинать игру. Если нет — в reason причина для UI.</summary>
        public bool CanStartGame(out string reason)
        {
            reason = null;

            if (Net == null || !Net.IsServer)
            {
                reason = "Начать игру может только хост";
                return false;
            }

            var players = NetworkPlayer.All;
            int minPlayers = Application.isEditor && allowSoloStartInEditor ? 1 : MinPlayersToStart;
            if (players.Count < minPlayers)
            {
                reason = $"Нужно минимум {MinPlayersToStart} игрока";
                return false;
            }

            foreach (var player in players)
            {
                if (!player.IsReady.Value)
                {
                    reason = "Не все игроки готовы";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Хост запускает игру: все переходят в хаб.</summary>
        public void StartGame()
        {
            if (GameStarted || !CanStartGame(out _)) return;

            GameStarted = true;
            LockSessionAsync();

            // Дирижёр сессии: рыба, раунды, переходы между сценами. Живёт до конца игры.
            if (sessionManagerPrefab != null)
                Instantiate(sessionManagerPrefab).Spawn();
            else
                Debug.LogError("[Maoshi] В ConnectionManager не задан префаб SessionManager (запусти Maoshi → Setup → Stage 3).");

            // Сетевая загрузка сцены: NGO сам загрузит её у всех клиентов.
            Net.SceneManager.LoadScene(hubScene, LoadSceneMode.Single);
        }

        /// <summary>Сервер забирает имя, которое клиент прислал при подключении.</summary>
        public string TakePendingName(ulong clientId)
        {
            if (_pendingNames.TryGetValue(clientId, out string playerName))
            {
                _pendingNames.Remove(clientId);
                return playerName;
            }

            return "Кот";
        }

        /// <summary>Чистит имя игрока: обрезает пробелы и длину, подставляет имя по умолчанию.</summary>
        public static string SanitizeName(string raw)
        {
            string result = (raw ?? string.Empty).Trim();
            if (result.Length == 0) result = "Кот";
            if (result.Length > MaxNameLength) result = result.Substring(0, MaxNameLength);
            return result;
        }

        // ───────────────────────── Внутренняя логика ─────────────────────────

        private void BeginConnecting(ConnectionMode mode, string playerName)
        {
            Mode = mode;
            // Со Steam имя — ник в Steam (поле в меню тоже его показывает).
            if (SteamService.Ready && !string.IsNullOrEmpty(SteamService.PlayerName)) playerName = SteamService.PlayerName;
            _localName = SanitizeName(playerName);
            LastError = null;
            SessionCode = null;
            GameStarted = false;

            // Имя уходит хосту вместе с запросом на подключение и проверяется в ApprovalCheck.
            Net.NetworkConfig.ConnectionApproval = true;
            Net.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(_localName);

            SetState(ConnectionState.Connecting);
        }

        /// <summary>
        /// Инициализация Unity Services и анонимный вход. Нужны только для онлайн-режима.
        /// Каждый запуск получает свой профиль, иначе две копии игры на одном ПК
        /// (или виртуальные игроки Multiplayer Play Mode) получат одинаковый PlayerId и не смогут играть вместе.
        /// </summary>
        private async Task EnsureServicesAsync()
        {
            if (_servicesReady) return;

            try
            {
                var options = new InitializationOptions();
                options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                _servicesReady = true;
            }
            catch (Exception e)
            {
                throw new Exception("Unity Services недоступны. Проверь, что проект привязан к Unity Cloud " +
                                    "(Edit → Project Settings → Services). Подробности: " + e.Message);
            }
        }

        // ───────────────────────── Steam ─────────────────────────

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // без 0/O и 1/I — не перепутать

        /// <summary>Хост через Steam: лобби Steam (код + приглашения) и relay-сокет Steam для Netcode.</summary>
        private async Task HostSteamAsync()
        {
            var created = await Steamworks.SteamMatchmaking.CreateLobbyAsync(MaxPlayers);
            if (!created.HasValue) throw new Exception("Steam не создал лобби. Проверь подключение к интернету.");
            var lobby = created.Value;
            _lobby = lobby;

            string code = "";
            var random = new System.Random();
            for (int i = 0; i < 6; i++) code += CodeAlphabet[random.Next(CodeAlphabet.Length)];

            // Публичное — чтобы находилось по коду (в общем списке игр его нет: ищем только по точному коду).
            lobby.SetPublic();
            lobby.SetJoinable(true);
            lobby.SetData("maoshi", "1");
            lobby.SetData("ver", Application.version);
            lobby.SetData("code", code);
            lobby.SetData("name", _localName);

            UseTransport(_steamTransport);
            if (!Net.StartHost())
                throw new Exception("не удалось запустить хост Steam.");

            SessionCode = code;
            SteamService.SetPresence("В лобби", lobby.Id.Value);
            Debug.Log($"[Maoshi Steam] Лобби создано, код {code}");
            NotifyChanged();
        }

        private async Task JoinSteamByCodeAsync(string code)
        {
            var lobbies = await Steamworks.SteamMatchmaking.LobbyList
                .WithKeyValue("maoshi", "1")
                .WithKeyValue("code", code)
                .FilterDistanceWorldwide()
                .WithMaxResults(5)
                .RequestAsync();

            if (lobbies == null || lobbies.Length == 0)
                throw new Exception($"игра с кодом {code} не найдена. Проверь код (буквы латинские).");
            await EnterSteamLobbyAsync(lobbies[0]);
        }

        /// <summary>Зайти в лобби Steam по id (приглашение друга, «Присоединиться» в Steam, запуск игры по приглашению).</summary>
        public async void JoinSteamLobby(ulong lobbyId, string playerName)
        {
            if (State != ConnectionState.Offline || !SteamService.Ready || lobbyId == 0) return;
            BeginConnecting(ConnectionMode.Online, playerName);

            try
            {
                var lobby = new Steamworks.Data.Lobby(lobbyId);
                await EnterSteamLobbyAsync(lobby);
            }
            catch (Exception e)
            {
                await FailAsync("Не удалось войти в игру: " + e.Message);
            }
        }

        private async Task EnterSteamLobbyAsync(Steamworks.Data.Lobby lobby)
        {
            var result = await lobby.Join();
            if (result != Steamworks.RoomEnter.Success)
                throw new Exception(result switch
                {
                    Steamworks.RoomEnter.Full => "лобби заполнено.",
                    Steamworks.RoomEnter.DoesntExist => "игра уже закрыта.",
                    Steamworks.RoomEnter.NotAllowed => "игра уже началась или закрыта для входа.",
                    _ => "Steam не пустил в лобби (" + result + ")."
                });
            _lobby = lobby;

            if (lobby.GetData("started") == "1") throw new Exception("игра уже началась.");
            string version = lobby.GetData("ver");
            if (!string.IsNullOrEmpty(version) && version != Application.version)
                throw new Exception($"у хоста другая версия игры ({version}, у тебя {Application.version}). Обновитесь в Steam.");

            ulong host = lobby.Owner.Id.Value;
            if (host == 0 || host == SteamService.MySteamId) throw new Exception("хост лобби не найден.");

            _steamTransport.TargetSteamId = host;
            UseTransport(_steamTransport);
            if (!Net.StartClient()) throw new Exception("не удалось подключиться через Steam.");

            SessionCode = lobby.GetData("code");
            SteamService.SetPresence("В лобби", lobby.Id.Value);
            NotifyChanged();
        }

        /// <summary>Окно Steam «Пригласить друзей» (запасная кнопка в лобби).</summary>
        public void InviteFriends()
        {
            if (_lobby.HasValue) SteamService.OpenInviteDialog(_lobby.Value.Id.Value);
        }

        /// <summary>Позвать одного друга из списка в лобби: приглашение Steam + сообщение с кодом.</summary>
        public bool InviteFriend(ulong friendId) =>
            _lobby.HasValue && SteamService.InviteFriend(_lobby.Value.Id.Value, friendId, SessionCode);

        /// <summary>Игрок нажал «Войти» на плашке приглашения: выходим из текущей игры (если есть) и идём к другу.</summary>
        public void AcceptInvite(ulong lobbyId) => OnSteamJoinRequested(lobbyId);

        /// <summary>Что видно друзьям в Steam: «В лобби», «Во дворе», «Играет: …».</summary>
        public void SetSteamStatus(string status)
        {
            if (_lobby.HasValue) SteamService.SetPresence(status, GameStarted ? 0 : _lobby.Value.Id.Value);
        }

        /// <summary>Сервер решает, пускать ли нового игрока.</summary>
        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                                   NetworkManager.ConnectionApprovalResponse response)
        {
            if (GameStarted)
            {
                Reject(response, "Игра уже началась");
                return;
            }

            if (Net.ConnectedClientsIds.Count >= MaxPlayers)
            {
                Reject(response, $"Лобби заполнено ({MaxPlayers} из {MaxPlayers})");
                return;
            }

            string playerName = SanitizeName(Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>()));
            _pendingNames[request.ClientNetworkId] = playerName;

            response.Approved = true;
            response.CreatePlayerObject = true; // спавним NetworkPlayer для этого клиента
            response.Pending = false;
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Reason = reason;
            response.Pending = false;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId == Net.LocalClientId)
                SetState(ConnectionState.Connected);
        }

        private void OnClientDisconnect(ulong clientId)
        {
            // На сервере: отключился кто-то из клиентов — просто забываем его данные.
            if (Net.IsServer && clientId != NetworkManager.ServerClientId)
            {
                _pendingNames.Remove(clientId);
                return;
            }

            // Мы уже вышли сами — это эхо нашего же выхода.
            if (State == ConnectionState.Offline) return;

            // Отключили нас: хост вышел, нас не пустили или не удалось достучаться.
            string reason = string.IsNullOrEmpty(Net.DisconnectReason)
                ? (State == ConnectionState.Connecting ? "Не удалось подключиться к хосту" : "Соединение с хостом потеряно")
                : Net.DisconnectReason;

            _ = FailAsync(reason);
        }

        private async Task FailAsync(string message)
        {
            Debug.LogWarning("[Maoshi] " + message);
            LastError = message;
            await CleanupAsync();
        }

        /// <summary>Закрывает сессию и сеть, возвращает в главное меню.</summary>
        private async Task CleanupAsync()
        {
            // Сначала переходим в Offline, чтобы OnClientDisconnect не принял наш выход за ошибку.
            SetState(ConnectionState.Offline);
            GameStarted = false;
            if (LanDiscovery.Instance != null) LanDiscovery.Instance.BeaconSource = null;
            SessionCode = null;
            _pendingNames.Clear();

            if (_lobby.HasValue)
            {
                try { _lobby.Value.Leave(); }
                catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Выход из лобби: " + e.Message); }
                _lobby = null;
            }
            SteamService.ClearPresence();

            if (_session != null)
            {
                var session = _session;
                _session = null;
                try
                {
                    await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Maoshi] Ошибка при выходе из сессии: " + e.Message);
                }
            }

            if (Net != null && Net.IsListening && !Net.ShutdownInProgress)
                Net.Shutdown();
            if (Net != null) UseTransport(Transport);

            if (!IsInMenu)
                SceneManager.LoadScene(mainMenuScene);

            NotifyChanged();
        }

        /// <summary>Закрываем онлайн-сессию от новых игроков после старта. Не критично: ApprovalCheck всё равно не пустит.</summary>
        private async void LockSessionAsync()
        {
            if (_lobby.HasValue)
            {
                // Лобби Steam: больше не заходят ни по коду, ни по приглашению.
                _lobby.Value.SetJoinable(false);
                _lobby.Value.SetData("started", "1");
            }
            if (_session == null || !_session.IsHost) return;

            try
            {
                var hostSession = _session.AsHost();
                hostSession.IsLocked = true;
                await hostSession.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi] Не удалось закрыть сессию: " + e.Message);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            // Статус в Steam у друзей: «В лобби» / «Во дворе» / «В мини-игре».
            if (_lobby.HasValue)
                SetSteamStatus(next.name == mainMenuScene ? "В лобби" : next.name == hubScene ? "Во дворе" : "В мини-игре");
            NotifyChanged();
        }

        private void SetState(ConnectionState newState)
        {
            if (State == newState) return;
            State = newState;
            NotifyChanged();
        }

        private void NotifyChanged() => StateChanged?.Invoke();
    }
}
