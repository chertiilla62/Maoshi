using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.Network
{
    /// <summary>
    /// Транспорт Netcode через сеть Steam (Steam Networking Sockets, P2P через ретрансляторы Valve).
    /// Хост открывает «relay-сокет», клиент подключается к нему по SteamID хоста (его берём из лобби Steam).
    /// IP игроков друг другу не видны, проброс портов не нужен, работает везде, где работает Steam. Бесплатно.
    ///
    /// clientId в Netcode = номер соединения Steam (у сервера всегда 0).
    /// </summary>
    public class SteamNetTransport : NetworkTransport
    {
        /// <summary>Клиент: SteamID хоста (владелец лобби).</summary>
        public ulong TargetSteamId { get; set; }

        public override ulong ServerClientId => 0;

        private Server _server;
        private Client _client;
        private readonly Dictionary<ulong, Connection> _connections = new();

        // Надёжные сообщения, которые Steam не принял сразу (буфер отправки полон) — досылаем по порядку.
        private readonly Dictionary<ulong, Queue<(byte[] data, SendType type)>> _pending = new();
        // Сборка больших сообщений, пришедших частями.
        private readonly Dictionary<ulong, List<byte>> _assembly = new();
        private bool _warnedBuffer;

        // Заголовок каждого сообщения: 0 — целое, 1 — часть (дальше будут ещё), 2 — последняя часть.
        private const byte Whole = 0, Part = 1, LastPart = 2;
        // Steam не принимает одно сообщение больше 512 КБ — режем на куски поменьше.
        private const int MaxChunk = 256 * 1024;
        // ───────────────────────── Сокеты Facepunch ─────────────────────────

        private class Server : SocketManager
        {
            public SteamNetTransport Owner;

            public override void OnConnecting(Connection connection, ConnectionInfo info) => connection.Accept();

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                Owner._connections[connection.Id] = connection;
                Owner.Raise(NetworkEvent.Connect, connection.Id, default);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                connection.Close();
                Owner._pending.Remove(connection.Id);
                Owner._assembly.Remove(connection.Id);
                if (Owner._connections.Remove(connection.Id))
                {
                    Debug.Log($"[Maoshi Steam] Игрок {connection.Id} отключился: {info.EndReason} ({info.State})");
                    Owner.Raise(NetworkEvent.Disconnect, connection.Id, default);
                }
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel) =>
                Owner.Incoming(connection.Id, data, size);
        }

        private class Client : Steamworks.ConnectionManager
        {
            public SteamNetTransport Owner;
            private bool _announced;

            public override void OnConnected(ConnectionInfo info)
            {
                base.OnConnected(info);
                _announced = true;
                Owner.Raise(NetworkEvent.Connect, Owner.ServerClientId, default);
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                base.OnDisconnected(info);
                // Даже если так и не подключились — Netcode должен узнать, что попытка провалилась.
                Owner.Raise(NetworkEvent.Disconnect, Owner.ServerClientId, default);
                _announced = false;
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) =>
                Owner.Incoming(Owner.ServerClientId, data, size);

            public bool Announced => _announced;
        }

        // ───────────────────────── NetworkTransport ─────────────────────────

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!SteamService.Ready) return;
            try
            {
                // По умолчанию у Steam буфер отправки 512 КБ на соединение: при старте игры хост разом шлёт
                // сцену двора, котов и объекты всем игрокам — буфер переполнялся и сообщения терялись.
                SteamNetworkingUtils.SendBufferSize = 16 * 1024 * 1024;
                SteamNetworkingUtils.SendRateMin = 512 * 1024;
                SteamNetworkingUtils.SendRateMax = 16 * 1024 * 1024;
            }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Настройки сети: " + e.Message); }
        }

        public override bool StartServer()
        {
            if (!SteamService.Ready) return false;
            _server = SteamNetworkingSockets.CreateRelaySocket<Server>(0);
            if (_server == null) return false;
            _server.Owner = this;
            return true;
        }

        public override bool StartClient()
        {
            if (!SteamService.Ready || TargetSteamId == 0) return false;
            _client = SteamNetworkingSockets.ConnectRelay<Client>(TargetSteamId, 0);
            if (_client == null) return false;
            _client.Owner = this;
            return true;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            var type = networkDelivery switch
            {
                NetworkDelivery.Unreliable => SendType.Unreliable | SendType.NoNagle,
                NetworkDelivery.UnreliableSequenced => SendType.Unreliable | SendType.NoNagle,
                NetworkDelivery.ReliableFragmentedSequenced => SendType.Reliable,
                _ => SendType.Reliable | SendType.NoNagle,
            };

            Connection connection;
            if (_server != null)
            {
                if (!_connections.TryGetValue(clientId, out connection)) return;
            }
            else if (_client != null) connection = _client.Connection;
            else return;

            bool reliable = (type & SendType.Reliable) != 0;
            if (payload.Count <= MaxChunk)
            {
                Push(clientId, connection, Frame(Whole, payload.Array, payload.Offset, payload.Count), type, reliable);
                return;
            }

            // Большое сообщение (синхронизация сцены) — частями, по порядку (надёжные сообщения Steam не меняют порядок).
            for (int offset = 0; offset < payload.Count; offset += MaxChunk)
            {
                int length = Math.Min(MaxChunk, payload.Count - offset);
                byte header = offset + length >= payload.Count ? LastPart : Part;
                Push(clientId, connection, Frame(header, payload.Array, payload.Offset + offset, length), SendType.Reliable, true);
            }
        }

        private static byte[] Frame(byte header, byte[] source, int offset, int length)
        {
            var data = new byte[length + 1];
            data[0] = header;
            Buffer.BlockCopy(source, offset, data, 1, length);
            return data;
        }

        /// <summary>Отправить; если надёжное и Steam сейчас не принял — в очередь (порядок сохраняется).</summary>
        private void Push(ulong clientId, Connection connection, byte[] data, SendType type, bool reliable)
        {
            if (_pending.TryGetValue(clientId, out var queue) && queue.Count > 0)
            {
                if (reliable) queue.Enqueue((data, type));
                return; // ненадёжное (позиции) при заторе — просто пропускаем
            }

            var result = connection.SendMessage(data, type);
            if (result == Result.OK || !reliable) return;

            if (!_pending.TryGetValue(clientId, out queue)) _pending[clientId] = queue = new Queue<(byte[], SendType)>();
            queue.Enqueue((data, type));
            if (!_warnedBuffer)
            {
                _warnedBuffer = true;
                Debug.LogWarning($"[Maoshi Steam] Steam не принял сообщение ({result}) — досылаем из очереди.");
            }
        }

        /// <summary>Дослать очередь: каждый кадр, пока Steam принимает.</summary>
        private void FlushPending()
        {
            if (_pending.Count == 0) return;
            foreach (var pair in _pending)
            {
                var queue = pair.Value;
                if (queue.Count == 0) continue;
                Connection connection;
                if (_server != null) { if (!_connections.TryGetValue(pair.Key, out connection)) { queue.Clear(); continue; } }
                else if (_client != null) connection = _client.Connection;
                else { queue.Clear(); continue; }

                while (queue.Count > 0)
                {
                    var (data, type) = queue.Peek();
                    if (connection.SendMessage(data, type) != Result.OK) break;
                    queue.Dequeue();
                }
            }
        }

        /// <summary>Входящее сообщение: целое — сразу в Netcode, части — собрать и отдать целиком.</summary>
        private void Incoming(ulong clientId, IntPtr data, int size)
        {
            if (size < 1) return;
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            byte header = bytes[0];

            if (header == Whole)
            {
                Raise(NetworkEvent.Data, clientId, new ArraySegment<byte>(bytes, 1, size - 1));
                return;
            }

            if (!_assembly.TryGetValue(clientId, out var buffer)) _assembly[clientId] = buffer = new List<byte>(size * 4);
            for (int i = 1; i < size; i++) buffer.Add(bytes[i]);
            if (header != LastPart) return;

            _assembly.Remove(clientId);
            Raise(NetworkEvent.Data, clientId, new ArraySegment<byte>(buffer.ToArray()));
        }

        // События приходят из Receive() в начале кадра Netcode (EarlyUpdate) — сразу передаём их в Netcode.
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        protected override void OnEarlyUpdate()
        {
            try
            {
                _server?.Receive(256);
                _client?.Receive(256);
                FlushPending();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi Steam] Приём: " + e.Message);
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (_connections.TryGetValue(clientId, out var connection))
            {
                connection.Flush();
                connection.Close(true);
                _connections.Remove(clientId);
            }
        }

        public override void DisconnectLocalClient()
        {
            _client?.Connection.Flush();
            _client?.Close(true);
            _client = null;
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            try
            {
                if (_server != null && _connections.TryGetValue(clientId, out var connection))
                    return (ulong)Mathf.Max(0, connection.QuickStatus().Ping);
                if (_client != null)
                    return (ulong)Mathf.Max(0, _client.Connection.QuickStatus().Ping);
            }
            catch (Exception) { /* соединение уже закрыто */ }
            return 0;
        }

        protected override void OnPostLateUpdate() => FlushPending();

        public override void Shutdown()
        {
            try
            {
                if (_server != null)
                {
                    foreach (var connection in _connections.Values) connection.Close(true);
                    _server.Close();
                }
                _client?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Maoshi Steam] Закрытие: " + e.Message);
            }

            _connections.Clear();
            _pending.Clear();
            _assembly.Clear();
            _warnedBuffer = false;
            _server = null;
            _client = null;
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        private void Raise(NetworkEvent type, ulong clientId, ArraySegment<byte> payload) =>
            InvokeOnTransportEvent(type, clientId, payload, Time.realtimeSinceStartup);

    }
}
