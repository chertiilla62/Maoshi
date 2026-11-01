using Maoshi.Network;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>Проверка сети из меню редактора (в Play, из главного меню): создать игру, выйти, список игр в сети.</summary>
    public static class MaoshiNetTest
    {
        [MenuItem("Maoshi/Test/Net - Host Online (Play)")]
        public static void HostOnline() => ConnectionManager.Instance?.HostGame(ConnectionMode.Online, "Тест");

        [MenuItem("Maoshi/Test/Net - Host Local (Play)")]
        public static void HostLocal() => ConnectionManager.Instance?.HostGame(ConnectionMode.Local, "Тест");

        [MenuItem("Maoshi/Test/Net - Leave (Play)")]
        public static void Leave() => ConnectionManager.Instance?.Leave();

        [MenuItem("Maoshi/Test/Net - Copy Joins My Online Code (Play)")]
        public static void CopyJoinsCode()
        {
            var c = ConnectionManager.Instance;
            if (c == null || string.IsNullOrEmpty(c.SessionCode)) { Debug.LogError("[Maoshi] Сначала Host Online"); return; }
            MaoshiTestBuild.RunCopy("-maoshiJoin " + c.SessionCode + " -maoshiName Копия");
        }

        [MenuItem("Maoshi/Test/Net - Copy Hosts Local")]
        public static void CopyHostsLocal() => MaoshiTestBuild.RunCopy("-maoshiHost local -maoshiName Копия");

        [MenuItem("Maoshi/Test/Net - Copy Joins LAN (auto)")]
        public static void CopyJoinsLan() => MaoshiTestBuild.RunCopy("-maoshiJoinLan 1 -maoshiName Копия");

        [MenuItem("Maoshi/Test/Net - Join First LAN Game (Play)")]
        public static void JoinFirstLan()
        {
            var lan = LanDiscovery.Instance;
            if (lan == null || lan.Games.Count == 0) { Debug.LogError("[Maoshi] Игр в сети не найдено"); return; }
            var g = lan.Games[0];
            ConnectionManager.Instance.JoinGame(ConnectionMode.Local, "Редактор", g.Address + ":" + g.Port);
        }

        [MenuItem("Maoshi/Test/Net - Beacon Self Test (Play)")]
        public static void BeaconSelf()
        {
            var lan = LanDiscovery.Instance;
            lan.StartListening();
            lan.BeaconSource = lan.BeaconSource == null ? () => ("Сам", 1, 8, (ushort)7777, false) : null;
            Debug.Log("[Maoshi] Маячок самому себе: " + (lan.BeaconSource != null ? "вкл" : "выкл"));
        }

        [MenuItem("Maoshi/Test/Net - Log State (Play)")]
        public static void LogState()
        {
            var c = ConnectionManager.Instance;
            if (c == null) { Debug.Log("[Maoshi] Нет ConnectionManager (не Play?)"); return; }
            Debug.Log($"[Maoshi] Сеть: {c.State}, режим {c.Mode}, код «{c.SessionCode}», ошибка «{c.LastError}», игроков {NetworkPlayer.All.Count}");
            foreach (var p in NetworkPlayer.All) Debug.Log($"[Maoshi]   игрок {p.DisplayName}");
            var lan = LanDiscovery.Instance;
            if (lan == null) return;
            if (!lan.IsListening) lan.StartListening();
            Debug.Log($"[Maoshi] В сети найдено игр: {lan.Games.Count}; слушаем {lan.IsListening}, пришло {lan.DebugReceived}, " +
                      $"разослано {lan.DebugSent}, последний «{lan.DebugLastText}», ошибка «{lan.DebugLastError}», версия {Application.version}");
            foreach (var g in lan.Games)
                Debug.Log($"[Maoshi]   {g.HostName} {g.Address}:{g.Port} {g.Players}/{g.MaxPlayers} {(g.Started ? "идёт" : "ждёт")}");
        }
    }
}
