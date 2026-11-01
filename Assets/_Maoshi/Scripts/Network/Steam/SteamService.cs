using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Maoshi.Network
{
    /// <summary>
    /// Steam (Facepunch.Steamworks): запуск клиента Steam, ник игрока, оверлей (Shift+Tab), приглашения друзей.
    /// Создаётся сам до загрузки первой сцены и живёт всю игру.
    ///
    /// Если Steam не запущен (или игра запущена с -nosteam) — Ready = false, игра работает как раньше:
    /// онлайн через Unity Relay по коду, имя вводится вручную.
    ///
    /// AppId: пока своего нет — 480 (тестовое приложение Valve «Spacewar»: у друзей в Steam игра видна как Spacewar).
    /// Когда Valve выдаст номер — поменять AppId здесь, и всё (приглашения, оверлей, лобби) заработает уже от имени Maoshi.
    /// </summary>
    public static class SteamService
    {
        public const uint AppId = 480;

        /// <summary>Steam запущен и клиент подключён.</summary>
        public static bool Ready { get; private set; }

        /// <summary>Почему Steam недоступен (для подсказки в меню).</summary>
        public static string Problem { get; private set; } = "";

        /// <summary>Ник в Steam (null, если Steam недоступен).</summary>
        public static string PlayerName => Ready ? SteamClient.Name : null;

        public static ulong MySteamId => Ready ? SteamClient.SteamId.Value : 0;

        /// <summary>Открыт оверлей Steam (Shift+Tab) — кот не должен слушаться клавиш.</summary>
        public static bool OverlayOpen { get; private set; }

        /// <summary>
        /// Друг пригласил в лобби (или игрок нажал «Присоединиться» в списке друзей): id лобби Steam.
        /// ConnectionManager подписан и заходит в игру.
        /// </summary>
        public static event Action<ulong> JoinLobbyRequested;

        /// <summary>Друг позвал в игру, пока Maoshi открыта (имя друга, id лобби) — показываем плашку в игре.</summary>
        public static event Action<string, ulong> InviteReceived;

        /// <summary>Тестовый номер Valve (Spacewar) — пока у игры нет своего.</summary>
        public static bool IsTestAppId => AppId == 480;

        private class Runner : MonoBehaviour
        {
            private void Update()
            {
                if (Ready) SteamClient.RunCallbacks();
            }

            private void OnApplicationQuit() => Shutdown();
            private void OnDestroy() => Shutdown();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Ready = false;
            OverlayOpen = false;
            Problem = "";

            if (Array.Exists(Environment.GetCommandLineArgs(), a => a.Equals("-nosteam", StringComparison.OrdinalIgnoreCase)))
            {
                Problem = "Steam выключен параметром -nosteam";
                return;
            }

            // Своя игра в Steam: запустили exe из папки, а не из Steam — Steam перезапускает игру через себя
            // (иначе не работает оверлей). Для теста (480) и при steam_appid.txt рядом с игрой — не перезапускаем.
            if (!Application.isEditor && !IsTestAppId && !File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "steam_appid.txt")))
            {
                try
                {
                    if (SteamClient.RestartAppIfNecessary(AppId))
                    {
                        Application.Quit();
                        return;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Maoshi Steam] RestartAppIfNecessary: " + e.Message); }
            }

            try
            {
                // Во время Play в редакторе Steam мог остаться запущенным с прошлого раза (без перезагрузки домена).
                if (!SteamClient.IsValid) SteamClient.Init(AppId, false);
                Ready = SteamClient.IsValid;
            }
            catch (Exception e)
            {
                // Steam не запущен, нет steam_api64.dll или не та учётка — играем без Steam.
                Problem = "Steam не запущен";
                Debug.LogWarning("[Maoshi Steam] Steam недоступен, онлайн через Unity Relay: " + e.Message);
                return;
            }

            if (!Ready) return;

            var go = new GameObject("SteamService");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<Runner>();

            // Сеть Steam: заранее подготовить доступ к ретрансляторам Valve (первое подключение быстрее).
            try { SteamNetworkingUtils.InitRelayNetworkAccess(); }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Relay: " + e.Message); }

            SteamFriends.OnGameOverlayActivated += active => OverlayOpen = active;
            // Принял приглашение (или «Присоединиться» в списке друзей при открытой игре).
            SteamFriends.OnGameLobbyJoinRequested += (lobby, friend) => JoinLobbyRequested?.Invoke(lobby.Id.Value);
            SteamFriends.OnGameRichPresenceJoinRequested += (friend, connect) =>
            {
                ulong id = ParseConnect(connect);
                if (id != 0) JoinLobbyRequested?.Invoke(id);
            };

            // Приглашение пришло, пока игра открыта: Steam сам покажет уведомление, а мы — плашку «Войти» в игре.
            SteamMatchmaking.OnLobbyInvite += (friend, lobby) => InviteReceived?.Invoke(friend.Name, lobby.Id.Value);
            // Нужно, чтобы отправлять друзьям сообщение с кодом вместе с приглашением.
            try { SteamFriends.ListenForFriendsMessages = true; } catch (Exception) { /* старый клиент Steam */ }

            Debug.Log($"[Maoshi Steam] Steam готов: {SteamClient.Name} (AppId {AppId})");
        }

        public static void Shutdown()
        {
            if (!Ready) return;
            Ready = false;
            try
            {
                SteamFriends.ClearRichPresence();
                SteamClient.Shutdown();
            }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Shutdown: " + e.Message); }
        }

        /// <summary>
        /// Лобби, в которое игру позвали при запуске: Steam запускает игру с «+connect_lobby &lt;id&gt;»,
        /// если друг принял приглашение, когда игра была закрыта.
        /// </summary>
        public static ulong LaunchLobby()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id)) return id;
            return 0;
        }

        /// <summary>Строка «+connect_lobby 1234» → id лобби.</summary>
        public static ulong ParseConnect(string connect)
        {
            if (string.IsNullOrEmpty(connect)) return 0;
            var parts = connect.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id)) return id;
            return 0;
        }

        /// <summary>Что показывать друзьям в Steam («Во дворе», «Играет: Горячий пирожок») и как к нам зайти.</summary>
        public static void SetPresence(string status, ulong lobbyId)
        {
            if (!Ready) return;
            SteamFriends.SetRichPresence("status", status ?? "");
            // «connect» включает кнопку «Присоединиться к игре» в списке друзей.
            SteamFriends.SetRichPresence("connect", lobbyId != 0 ? "+connect_lobby " + lobbyId : "");
        }

        public static void ClearPresence()
        {
            if (Ready) SteamFriends.ClearRichPresence();
        }

        // ───────────────────────── Друзья ─────────────────────────

        public enum FriendStatus { InThisGame, Online, Away }

        public struct FriendInfo
        {
            public ulong Id;
            public string Name;
            public FriendStatus Status;
        }

        /// <summary>Друзья в сети: сначала те, кто уже в Maoshi, потом в сети, потом «отошёл».</summary>
        public static List<FriendInfo> OnlineFriends()
        {
            var list = new List<FriendInfo>();
            if (!Ready) return list;
            try
            {
                foreach (var friend in SteamFriends.GetFriends())
                {
                    if (!friend.IsOnline) continue;
                    var status = friend.IsPlayingThisGame ? FriendStatus.InThisGame
                        : friend.IsAway || friend.IsSnoozing ? FriendStatus.Away : FriendStatus.Online;
                    list.Add(new FriendInfo { Id = friend.Id.Value, Name = friend.Name, Status = status });
                }
            }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Друзья: " + e.Message); }

            list.Sort((a, b) => a.Status != b.Status ? a.Status.CompareTo(b.Status) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Позвать друга: приглашение Steam в лобби (в чате Steam у друга — карточка «Присоединиться») +
        /// сообщение в чат с кодом комнаты (если Steam разрешит — для некоторых клиентов чат из игр закрыт).
        /// </summary>
        public static bool InviteFriend(ulong lobbyId, ulong friendId, string code)
        {
            if (!Ready || lobbyId == 0) return false;
            bool invited = false;
            try
            {
                invited = new Steamworks.Data.Lobby(lobbyId).InviteFriend(friendId);
                string text = string.IsNullOrEmpty(code)
                    ? "Залетай ко мне в Maoshi! Жми «Присоединиться» в приглашении."
                    : $"Залетай ко мне в Maoshi! Жми «Присоединиться» в приглашении или введи код комнаты: {code}";
                new Friend(friendId).SendMessage(text);
            }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Приглашение: " + e.Message); }
            return invited;
        }

        private static readonly Dictionary<ulong, Texture2D> Avatars = new();
        private static readonly HashSet<ulong> AvatarsLoading = new();

        /// <summary>Аватар друга (маленький, 32×32). null — ещё грузится; потом вызывается onLoaded.</summary>
        public static Texture2D Avatar(ulong steamId, Action onLoaded)
        {
            if (Avatars.TryGetValue(steamId, out var texture)) return texture;
            if (Ready && AvatarsLoading.Add(steamId)) _ = LoadAvatar(steamId, onLoaded);
            return null;
        }

        private static async Task LoadAvatar(ulong steamId, Action onLoaded)
        {
            try
            {
                var image = await SteamFriends.GetMediumAvatarAsync(steamId);
                if (!image.HasValue) return;
                var img = image.Value;
                var texture = new Texture2D((int)img.Width, (int)img.Height, TextureFormat.RGBA32, false) { name = "SteamAvatar" };
                // У Steam строки сверху вниз, у Unity — снизу вверх.
                int row = (int)img.Width * 4;
                var flipped = new byte[img.Data.Length];
                for (int y = 0; y < img.Height; y++)
                    Buffer.BlockCopy(img.Data, y * row, flipped, ((int)img.Height - 1 - y) * row, row);
                texture.LoadRawTextureData(flipped);
                texture.Apply();
                Avatars[steamId] = texture;
                onLoaded?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning("[Maoshi Steam] Аватар: " + e.Message); }
        }

        /// <summary>Окно Steam «Пригласить друзей» в это лобби.</summary>
        public static void OpenInviteDialog(ulong lobbyId)
        {
            if (Ready && lobbyId != 0) SteamFriends.OpenGameInviteOverlay(lobbyId);
        }
    }
}
