using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Maoshi.Network
{
    /// <summary>
    /// Поиск игр в локальной сети — чтобы не вводить IP руками.
    /// - Хост локальной игры раз в секунду рассылает по сети короткий «маячок» (UDP broadcast на порт DiscoveryPort):
    ///   «MAOSHI|версия|имя хоста|игроков|максимум|порт игры|идёт ли игра|номер хоста».
    ///   Шлём на общий адрес 255.255.255.255 и на широковещательный адрес каждой сети компьютера
    ///   (если у ПК несколько адаптеров — Wi-Fi, кабель, VPN, — Windows иначе шлёт только в один).
    /// - Главное меню слушает этот порт (несколько копий игры на одном ПК могут слушать одновременно) и
    ///   показывает найденные игры; адрес хоста берётся из того, откуда пришёл маячок.
    /// Маячки других версий игры не показываем — с ними всё равно не сыграть.
    /// Приём — без потоков: в Update забираем всё, что пришло в сокет (неблокирующе), поэтому ничего
    /// не мешает перезагрузке скриптов в редакторе.
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        private const string Magic = "MAOSHI";
        private const float BeaconInterval = 1f;
        private const float GameTimeout = 3.5f;

        /// <summary>Найденная в сети игра.</summary>
        public struct FoundGame
        {
            public string Id;
            public string Address;
            public ushort Port;
            public string HostName;
            public int Players;
            public int MaxPlayers;
            public bool Started;
            public float SeenAt;
            public string Key => Id;
        }

        public static LanDiscovery Instance { get; private set; }

        private readonly Dictionary<string, FoundGame> _games = new();
        private readonly List<string> _expired = new();
        private readonly List<FoundGame> _snapshot = new();
        private UdpClient _listener;
        private bool _listening;
        private string _version;

        private UdpClient _sender;
        private float _nextBeacon;
        private List<IPEndPoint> _targets;
        private float _targetsAt = -100f;
        // Номер этого хоста: один и тот же маячок приходит разными путями (сеть, 127.0.0.1) — склеиваем по нему.
        private readonly string _hostId = Guid.NewGuid().ToString("N").Substring(0, 10);

        /// <summary>Описание маячка хоста; null — не рассылать.</summary>
        public Func<(string hostName, int players, int maxPlayers, ushort port, bool started)> BeaconSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _version = Application.version;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopListening();
            _sender?.Close();
            _sender = null;
        }

        private void OnApplicationQuit() => StopListening();

        private void Update()
        {
            if (_listening) Poll();

            var beacon = BeaconSource;
            if (beacon != null && Time.realtimeSinceStartup >= _nextBeacon)
            {
                _nextBeacon = Time.realtimeSinceStartup + BeaconInterval;
                SendBeacon(beacon());
            }
        }

        // ───────────────────────── Поиск (клиент) ─────────────────────────

        /// <summary>Начать слушать маячки (главное меню, вкладка «По локальной сети»).</summary>
        public void StartListening()
        {
            if (_listening) return;
            try
            {
                _listener = new UdpClient { ExclusiveAddressUse = false, EnableBroadcast = true };
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _listener.Client.Blocking = false;
                _listening = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi] Поиск игр в сети недоступен: " + e.Message);
                _listener?.Close();
                _listener = null;
                _listening = false;
            }
        }

        public void StopListening()
        {
            _listening = false;
            try { _listener?.Close(); } catch { /* закрываем как есть */ }
            _listener = null;
            _games.Clear();
        }

        public bool IsListening => _listening;

        // Для отладки: сколько пакетов пришло, последний текст и ошибка приёма, сколько маячков разослано.
        public int DebugReceived, DebugSent;
        public string DebugLastText = "", DebugLastError = "";

        /// <summary>Забрать все пришедшие маячки (не ждёт, если ничего нет).</summary>
        private void Poll()
        {
            if (_listener == null) return;
            try
            {
                for (int guard = 0; guard < 64 && _listener.Available > 0; guard++)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref from);
                    string text = Encoding.UTF8.GetString(data);
                    DebugReceived++;
                    DebugLastText = from.Address + " " + text;
                    Parse(text, from.Address);
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                // нечего читать
            }
            catch (Exception e)
            {
                DebugLastError = e.GetType().Name + ": " + e.Message;
            }
        }

        private void Parse(string text, IPAddress address)
        {
            string[] parts = text.Split('|');
            if (parts.Length < 8 || parts[0] != Magic || parts[1] != _version) return;
            if (!ushort.TryParse(parts[5], out ushort port)) return;
            int.TryParse(parts[3], out int players);
            int.TryParse(parts[4], out int max);
            var game = new FoundGame
            {
                Id = parts[7],
                Address = address.ToString(),
                Port = port,
                HostName = parts[2],
                Players = players,
                MaxPlayers = max,
                Started = parts[6] == "1",
                SeenAt = Time.realtimeSinceStartup
            };
            // Один хост слышен с разных адаптеров (сеть, виртуальные сети WSL/VPN, 127.0.0.1) —
            // оставляем самый «домашний» адрес: его понятнее показать и он подходит другим ПК.
            if (_games.TryGetValue(game.Id, out var known) && known.Address != game.Address &&
                AddressRank(known.Address) > AddressRank(game.Address))
                game.Address = known.Address;
            _games[game.Id] = game;
        }

        /// <summary>Насколько адрес похож на обычную домашнюю сеть (больше — лучше).</summary>
        private static int AddressRank(string address)
        {
            if (address.StartsWith("192.168.") || address.StartsWith("10.")) return 3;
            if (address.StartsWith("26.") || address.StartsWith("25.")) return 2; // Radmin VPN / Hamachi
            if (address.StartsWith("127.")) return 0;
            return 1; // 172.16–31 — чаще всего виртуальные адаптеры (WSL, Hyper-V, Docker)
        }

        /// <summary>Свежие игры в сети (сначала те, куда можно войти). Вызывать из главного потока.</summary>
        public IReadOnlyList<FoundGame> Games
        {
            get
            {
                _snapshot.Clear();
                _expired.Clear();
                float now = Time.realtimeSinceStartup;
                foreach (var pair in _games)
                {
                    if (now - pair.Value.SeenAt > GameTimeout) _expired.Add(pair.Key);
                    else _snapshot.Add(pair.Value);
                }
                foreach (string key in _expired) _games.Remove(key);
                _snapshot.Sort((a, b) =>
                {
                    int byStarted = a.Started.CompareTo(b.Started);
                    return byStarted != 0 ? byStarted : string.CompareOrdinal(a.HostName, b.HostName);
                });
                return _snapshot;
            }
        }

        // ───────────────────────── Маячок (хост) ─────────────────────────

        private void SendBeacon((string hostName, int players, int maxPlayers, ushort port, bool started) info)
        {
            try
            {
                _sender ??= new UdpClient { EnableBroadcast = true };
                string name = (info.hostName ?? "Кот").Replace("|", "/");
                string text = $"{Magic}|{_version}|{name}|{info.players}|{info.maxPlayers}|{info.port}|{(info.started ? 1 : 0)}|{_hostId}";
                byte[] data = Encoding.UTF8.GetBytes(text);

                if (_targets == null || Time.realtimeSinceStartup - _targetsAt > 10f)
                {
                    _targets = BroadcastTargets();
                    _targetsAt = Time.realtimeSinceStartup;
                }
                foreach (var target in _targets)
                {
                    try { _sender.Send(data, data.Length, target); DebugSent++; }
                    catch (Exception e) { DebugLastError = "send " + target + ": " + e.Message; }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi] Не удалось разослать маячок игры: " + e.Message);
            }
        }

        /// <summary>Куда слать маячок: общий широковещательный адрес, адрес каждой сети ПК и сам ПК (127.0.0.1).</summary>
        private static List<IPEndPoint> BroadcastTargets()
        {
            var set = new HashSet<string> { IPAddress.Broadcast.ToString(), IPAddress.Loopback.ToString() };
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        byte[] ip = unicast.Address.GetAddressBytes();
                        byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                        if (ip.Length != 4 || mask.Length != 4) continue;
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        set.Add(new IPAddress(broadcast).ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi] Не удалось получить список сетей: " + e.Message);
            }

            var list = new List<IPEndPoint>();
            foreach (string address in set) list.Add(new IPEndPoint(IPAddress.Parse(address), DiscoveryPort));
            return list;
        }

        /// <summary>IP этого компьютера в локальной сети (для подсказки «зайти вручную»).</summary>
        public static string LocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = unicast.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue; // адрес без сети
                        if (!result.Contains(ip)) result.Add(ip);
                    }
                }
            }
            catch
            {
                // нет доступа к списку адаптеров — подсказки не будет
            }
            return string.Join(", ", result);
        }
    }
}
