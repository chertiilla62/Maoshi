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
                if (Owner._connections.Remove(connection.Id))
                    Owner.Raise(NetworkEvent.Disconnect, connection.Id, default);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel) =>
                Owner.Raise(NetworkEvent.Data, connection.Id, Owner.Copy(data, size));
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
                Owner.Raise(NetworkEvent.Data, Owner.ServerClientId, Owner.Copy(data, size));

            public bool Announced => _announced;
        }

        // ───────────────────────── NetworkTransport ─────────────────────────

        public override void Initialize(NetworkManager networkManager = null) { }

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

            if (_server != null)
            {
                if (_connections.TryGetValue(clientId, out var connection))
                    connection.SendMessage(payload.Array, payload.Offset, payload.Count, type);
            }
            else if (_client != null)
            {
                _client.Connection.SendMessage(payload.Array, payload.Offset, payload.Count, type);
            }
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
                _server?.Receive(64);
                _client?.Receive(64);
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
            _server = null;
            _client = null;
        }

        // ───────────────────────── Вспомогательное ─────────────────────────

        private void Raise(NetworkEvent type, ulong clientId, ArraySegment<byte> payload) =>
            InvokeOnTransportEvent(type, clientId, payload, Time.realtimeSinceStartup);

        /// <summary>Копия сообщения из памяти Steam (её освобождают сразу после колбэка).</summary>
        private ArraySegment<byte> Copy(IntPtr data, int size)
        {
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            return new ArraySegment<byte>(bytes);
        }
    }
}
