using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.Gameplay
{
    /// <summary>
    /// Ставится в каждую игровую сцену. Когда сцена загрузилась у всех,
    /// сервер создаёт по коту на каждого игрока и отдаёт кота ему во владение.
    /// Коты расставляются по кругу вокруг точки спавна.
    /// </summary>
    public class CatSpawner : NetworkBehaviour
    {
        /// <summary>Как расставлять котов.</summary>
        public enum SpawnLayout
        {
            /// <summary>По кругу, мордой к центру.</summary>
            Circle,
            /// <summary>В линию вдоль оси Z объекта, парами мордой друг к другу (забор, бревно).</summary>
            Line,
            /// <summary>Шеренгой вдоль оси X объекта, все смотрят вперёд по Z (старт гонки).</summary>
            Row,
            /// <summary>Заданные точки (кочки, места): кот i — на точку i (по кругу, если котов больше), мордой к центру спавнера.</summary>
            Points
        }

        [SerializeField] private NetworkObject catPrefab;
        [SerializeField] private SpawnLayout layout = SpawnLayout.Circle;
        [SerializeField] private float spawnRadius = 4f;
        [SerializeField] private float lineSpacing = 1.4f;
        [SerializeField] private float spawnHeight = 0.5f;
        [Tooltip("Для раскладки Points: точки появления по порядку.")]
        [SerializeField] private Transform[] points;

        private readonly HashSet<ulong> _spawnedFor = new();
        private readonly Dictionary<ulong, (Vector3 position, Quaternion rotation)> _poses = new();
        // Игроки, не успевшие загрузить сцену к общему событию: их место в раскладке, кот — когда догрузятся.
        private readonly Dictionary<ulong, (int index, int total)> _late = new();

        /// <summary>Спавнер текущей сцены (сервер и клиенты).</summary>
        public static CatSpawner Current { get; private set; }

        public override void OnNetworkSpawn()
        {
            Current = this;
            if (!IsServer) return;
            NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            NetworkManager.SceneManager.OnSceneEvent += OnSceneEvent;
        }

        public override void OnNetworkDespawn()
        {
            if (Current == this) Current = null;
            if (NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
                NetworkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name) return;

            // Места раздаём всем (и опоздавшим), чтобы раскладка не съезжала.
            var clients = new List<ulong>(clientsCompleted);
            foreach (ulong late in clientsTimedOut) if (!clients.Contains(late)) clients.Add(late);
            clients.Sort();

            for (int i = 0; i < clients.Count; i++)
            {
                if (clientsCompleted.Contains(clients[i])) SpawnCat(clients[i], i, clients.Count);
                else _late[clients[i]] = (i, clients.Count);
            }

            if (clientsTimedOut.Count > 0)
                Debug.LogWarning($"[Maoshi] {sceneName}: не успели загрузиться игроки {string.Join(", ", clientsTimedOut)} — кот появится, когда догрузятся.");
        }

        /// <summary>Сервер: игрок догрузил сцену позже остальных — создаём его кота сейчас.</summary>
        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType != SceneEventType.LoadComplete || sceneEvent.SceneName != gameObject.scene.name) return;
            ulong clientId = sceneEvent.ClientId;
            if (clientId == NetworkManager.ServerClientId || _spawnedFor.Contains(clientId)) return;
            if (!_late.TryGetValue(clientId, out var slot)) return; // обычный случай: кот придёт с общим событием

            _late.Remove(clientId);
            Debug.Log($"[Maoshi] {sceneEvent.SceneName}: игрок {clientId} догрузился — создаём кота.");
            SpawnCat(clientId, slot.index, slot.total);
        }

        /// <summary>Сервер: у подключённого игрока нет кота в этой сцене (консоль, команда respawn).</summary>
        public bool EnsureCat(ulong clientId)
        {
            if (!IsServer || Maoshi.Cat.CatController.FindByOwner(clientId) != null) return false;
            _spawnedFor.Remove(clientId);
            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            ids.Sort();
            int index = Mathf.Max(0, ids.IndexOf(clientId));
            _late.Remove(clientId);
            SpawnCat(clientId, index, ids.Count);
            return true;
        }

        private void SpawnCat(ulong clientId, int index, int total)
        {
            if (catPrefab == null)
            {
                Debug.LogError("[Maoshi] CatSpawner: не задан префаб кота.");
                return;
            }

            if (!_spawnedFor.Add(clientId)) return;

            GetSpawnPose(index, total, out Vector3 position, out Quaternion rotation);

            var cat = Instantiate(catPrefab, position, rotation);
            cat.SpawnWithOwnership(clientId, destroyWithScene: true);
            _poses[clientId] = (position, rotation);

            // Владелец сам ставит кота на точку (и запоминает её как точку возврата): у клиента по сети
            // кот мог появиться не там, где задумано, — тогда он стоял бы вне площадки или в мебели.
            if (cat.TryGetComponent(out Maoshi.Cat.CatController controller))
                controller.SetRespawnAndTeleportRpc(position, rotation);
        }

        /// <summary>Сервер: вернуть кота на его точку появления.</summary>
        public bool ReturnToSpawn(Maoshi.Cat.CatController cat)
        {
            if (!IsServer || cat == null || !_poses.TryGetValue(cat.OwnerClientId, out var pose)) return false;
            cat.SetRespawnAndTeleportRpc(pose.position, pose.rotation);
            return true;
        }

        /// <summary>Сервер: все коты — на свои точки (перед стартом мини-игры).</summary>
        public void ReturnAllToSpawn()
        {
            if (!IsServer) return;
            foreach (var cat in Maoshi.Cat.CatController.All) ReturnToSpawn(cat);
        }

        private void GetSpawnPose(int index, int total, out Vector3 position, out Quaternion rotation)
        {
            Vector3 center = transform.position;

            if (layout == SpawnLayout.Points && points != null && points.Length > 0)
            {
                var point = points[index % points.Length];
                position = point.position + Vector3.up * spawnHeight;
                Vector3 toMiddle = Vector3.ProjectOnPlane(center - point.position, Vector3.up);
                rotation = toMiddle.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toMiddle) : point.rotation;
                return;
            }

            if (layout == SpawnLayout.Row)
            {
                float offset = (index - (total - 1) * 0.5f) * lineSpacing;
                position = center + transform.right * offset + Vector3.up * spawnHeight;
                rotation = Quaternion.LookRotation(transform.forward);
                return;
            }

            if (layout == SpawnLayout.Line)
            {
                // Центрируем линию; чётные смотрят вперёд, нечётные назад — пары стоят нос к носу.
                float offset = (index - (total - 1) * 0.5f) * lineSpacing;
                position = center + transform.forward * offset + Vector3.up * spawnHeight;
                rotation = Quaternion.LookRotation(index % 2 == 0 ? transform.forward : -transform.forward);
                return;
            }

            // Равномерно по кругу, мордой к центру.
            float angle = 360f * index / Mathf.Max(1, total);
            position = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRadius + Vector3.up * spawnHeight;
            Vector3 toCenter = Vector3.ProjectOnPlane(center - position, Vector3.up);
            rotation = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f);

            if (layout != SpawnLayout.Circle)
            {
                for (int i = 0; i < 8; i++)
                {
                    GetSpawnPose(i, 8, out Vector3 position, out _);
                    Gizmos.DrawWireSphere(position, 0.3f);
                }
                return;
            }

            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = transform.position + Quaternion.Euler(0f, 360f * i / segments, 0f) * Vector3.forward * spawnRadius;
                Vector3 b = transform.position + Quaternion.Euler(0f, 360f * (i + 1) / segments, 0f) * Vector3.forward * spawnRadius;
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
