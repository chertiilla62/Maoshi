using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.Settings;
using Maoshi.Showdown;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi
{
    /// <summary>
    /// Консоль разработчика: команды и журнал. Окно рисует DevConsoleView (MaoshiUI), открывается клавишей ` (Ё) или F1.
    /// Доступна в редакторе и в отладочных сборках; в обычной (Steam) сборке — только с параметром запуска -console.
    /// Команды, меняющие игру (мини-игры, рыба, оглушение), выполняет хост — у клиента они отвечают «только хост».
    /// </summary>
    public static class DevConsole
    {
        public enum Kind { Info, Ok, Warn, Error, Input }

        public readonly struct Line
        {
            public readonly string Text;
            public readonly Kind Kind;
            public Line(string text, Kind kind) { Text = text; Kind = kind; }
        }

        private class Command
        {
            public string Name, Usage, Help;
            public string[] Aliases;
            public bool HostOnly;
            public Action<string[]> Run;
        }

        private const int MaxLines = 300;

        public static bool IsOpen { get; set; }

        /// <summary>Можно ли вообще открыть консоль.</summary>
        public static bool Enabled { get; private set; }

        public static IReadOnlyList<Line> Lines => _lines;
        public static event Action LinesChanged;

        private static readonly List<Line> _lines = new();
        private static readonly List<Command> _commands = new();
        private static readonly List<string> _history = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            IsOpen = false;
            _lines.Clear();
            _commands.Clear();
            Enabled = Application.isEditor || Debug.isDebugBuild ||
                      Environment.GetCommandLineArgs().Any(a => a.Equals("-console", StringComparison.OrdinalIgnoreCase));
            Register();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            Print("Консоль Maoshi. commands — список команд, ↑/↓ — история, Tab — дописать команду.", Kind.Info);
        }

        // ───────────────────────── Журнал ─────────────────────────

        public static void Print(string text, Kind kind = Kind.Info)
        {
            foreach (var part in text.Split('\n'))
                _lines.Add(new Line(part, kind));
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
            LinesChanged?.Invoke();
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            // В консоль попадают только ошибки и предупреждения игры (обычные логи — в Player.log).
            if (type == LogType.Log) return;
            string first = message.Split('\n')[0];
            if (first.Length > 220) first = first.Substring(0, 220) + "…";
            Print(first, type == LogType.Warning ? Kind.Warn : Kind.Error);
        }

        // ───────────────────────── Выполнение ─────────────────────────

        public static void Execute(string input)
        {
            input = input?.Trim() ?? "";
            if (input.Length == 0) return;
            if (_history.Count == 0 || _history[^1] != input) _history.Add(input);
            Print("> " + input, Kind.Input);

            var parts = input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0].ToLowerInvariant();
            var args = parts.Skip(1).ToArray();

            var command = Find(name);
            if (command == null)
            {
                Print($"Нет команды «{name}». Список — commands.", Kind.Error);
                return;
            }

            if (command.HostOnly && !IsHost)
            {
                Print(NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient
                    ? "Эту команду может выполнить только хост."
                    : "Сначала создай игру (команда работает у хоста).", Kind.Error);
                return;
            }

            try { command.Run(args); }
            catch (Exception e) { Print($"Ошибка: {e.Message}", Kind.Error); }
        }

        /// <summary>История ввода для ↑/↓: index от конца (0 — последняя).</summary>
        public static string History(int index) =>
            index >= 0 && index < _history.Count ? _history[_history.Count - 1 - index] : null;

        public static int HistoryCount => _history.Count;

        /// <summary>Tab: дописать имя команды, если начало совпадает с одной; если с несколькими — напечатать их.</summary>
        public static string Complete(string input)
        {
            string trimmed = input.TrimStart();
            if (trimmed.Contains(' ')) return input;
            var names = _commands.SelectMany(c => new[] { c.Name }.Concat(c.Aliases))
                .Where(n => n.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            if (names.Count == 1) return names[0] + " ";
            if (names.Count > 1) Print(string.Join("   ", names), Kind.Info);
            return input;
        }

        private static Command Find(string name) =>
            _commands.FirstOrDefault(c => c.Name == name || c.Aliases.Contains(name));

        private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        private static void Add(string name, string usage, string help, bool hostOnly, Action<string[]> run, params string[] aliases) =>
            _commands.Add(new Command { Name = name, Usage = usage, Help = help, HostOnly = hostOnly, Run = run, Aliases = aliases });

        // ───────────────────────── Команды ─────────────────────────

        private static void Register()
        {
            Add("commands", "commands", "список всех команд", false, _ => Commands(), "help", "?");
            Add("clear", "clear", "очистить консоль", false, _ => { _lines.Clear(); LinesChanged?.Invoke(); }, "cls");

            Add("list", "list", "все мини-игры: номер, id, название", false, _ => ListGames(), "games");
            Add("play", "play <номер|id>", "запустить мини-игру (хост, во дворе)", true, Play, "start");
            Add("end", "end", "досрочно закончить текущую мини-игру (итоги по текущим очкам)", true, _ => End(), "finish");
            Add("brawl", "brawl", "тренировочная «Кошачья свалка» с ботами (хост, во дворе)", true, _ => Brawl());
            Add("final", "final", "сразу финал партии со случайной статистикой (хост, во дворе)", true, _ => Final());
            Add("status", "status", "где сейчас игра: фаза, раунд, мини-игра", false, _ => Status());

            Add("players", "players", "игроки: номер, имя, рыба, зоны", false, _ => Players(), "who");
            Add("fish", "fish <число> [игрок|all]", "дать рыбу (минус — забрать). Игрок — номер из players или начало имени; без него — себе", true, Fish, "givefish");
            Add("stun", "stun [игрок|all]", "оглушить кота (без игрока — своего)", true, Stun);
            Add("tp", "tp <игрок>", "перенести своего кота к другому коту", false, Teleport, "goto");

            Add("cans", "cans <число>", "добавить консервные банки (магазин, хранятся на этом ПК)", false, Cans);
            Add("quality", "quality <0-3>", "общее качество: 0 низкое, 1 среднее, 2 высокое, 3 ультра", false, Quality);
            Add("fps", "fps", "показать/скрыть FPS и пинг", false, _ => Toggle("FPS и пинг", () => GameSettings.ShowNetStats = !GameSettings.ShowNetStats, () => GameSettings.ShowNetStats));
            Add("version", "version", "версия игры и Unity", false, _ => Print($"Maoshi {Application.version} · Unity {Application.unityVersion} · {(Debug.isDebugBuild ? "отладочная" : "релизная")} сборка", Kind.Info));
        }

        private static void Commands()
        {
            Print("Команды (★ — только хост):", Kind.Ok);
            foreach (var c in _commands)
            {
                string aliases = c.Aliases.Length > 0 ? $"  [{string.Join(", ", c.Aliases)}]" : "";
                Print($"  {(c.HostOnly ? "★ " : "")}{c.Usage} — {c.Help}{aliases}", Kind.Info);
            }
        }

        private static IReadOnlyList<MiniGameDefinition> Games =>
            SessionManager.Instance != null && SessionManager.Instance.Catalog != null
                ? SessionManager.Instance.Catalog.MiniGames
                : Resources.FindObjectsOfTypeAll<MiniGameCatalog>().FirstOrDefault()?.MiniGames;

        private static void ListGames()
        {
            var games = Games;
            if (games == null || games.Count == 0)
            {
                Print("Каталог мини-игр не найден (зайди в игру).", Kind.Warn);
                return;
            }

            Print($"Мини-игры ({games.Count}):", Kind.Ok);
            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                if (g == null) continue;
                string min = g.MinPlayers > 2 ? $"  · от {g.MinPlayers} игроков" : "";
                Print($"  {i + 1,2}. {g.Id,-16} {g.DisplayName}{min}", Kind.Info);
            }
            Print("Запуск: play <номер> или play <id>.", Kind.Info);
        }

        private static void Play(string[] args)
        {
            var session = SessionManager.Instance;
            if (args.Length == 0) { Print("Укажи игру: play <номер|id>. Список — list.", Kind.Warn); return; }
            if (session == null || session.Catalog == null) { Print("Нет сессии — начни игру во дворе.", Kind.Error); return; }

            var games = session.Catalog.MiniGames;
            MiniGameDefinition game = null;
            if (int.TryParse(args[0], out int number) && number >= 1 && number <= games.Count) game = games[number - 1];
            else
            {
                string query = string.Join(" ", args);
                game = games.FirstOrDefault(g => g != null && g.Id.Equals(query, StringComparison.OrdinalIgnoreCase))
                       ?? games.FirstOrDefault(g => g != null && (g.Id.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                                                                 || g.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (game == null) { Print($"Мини-игра «{args[0]}» не найдена. Список — list.", Kind.Error); return; }
            if (!session.CanStartMiniGame)
            {
                string hint = session.Phase.Value == SessionPhase.MiniGame ? " Сначала end — закончить текущую игру." : " Подожди, пока вернётесь во двор.";
                Print($"Сейчас нельзя: {PhaseName(session.Phase.Value)}. Мини-игру запускают только во дворе.{hint}", Kind.Error);
                return;
            }

            session.StartMiniGame(game.Id);
            Print($"Запускаю «{game.DisplayName}»…", Kind.Ok);
        }

        private static void End()
        {
            var mode = MiniGameMode.Current;
            if (mode == null || !mode.IsSpawned) { Print("Сейчас не идёт мини-игра.", Kind.Warn); return; }
            Print(mode.DebugFinish() ? "Мини-игра закончена — сейчас будут итоги." : "Игра уже заканчивается.", Kind.Ok);
        }

        private static void Brawl()
        {
            var showdown = ShowdownManager.Instance;
            var session = SessionManager.Instance;
            if (showdown == null || session == null || session.Phase.Value != SessionPhase.Hub || showdown.IsActive)
            {
                Print("Свалку можно начать только во дворе, когда она не идёт.", Kind.Error);
                return;
            }
            showdown.DebugStartVsBots();
            Print("Тренировочная свалка с ботами.", Kind.Ok);
        }

        private static void Final()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.DebugFinal()) { Print("Финал можно вызвать только во дворе.", Kind.Error); return; }
            Print("Переходим в финал.", Kind.Ok);
        }

        private static void Status()
        {
            var session = SessionManager.Instance;
            var connection = ConnectionManager.Instance;
            if (session == null)
            {
                Print(connection != null ? $"Не в игре (состояние: {connection.State})." : "Не в игре.", Kind.Info);
                return;
            }
            string game = session.CurrentMiniGameId.Value.ToString();
            var mode = MiniGameMode.Current;
            Print($"Сейчас: {PhaseName(session.Phase.Value)} · раунд {session.RoundNumber.Value}/{session.TotalRounds.Value}" +
                  (session.Phase.Value == SessionPhase.MiniGame ? $" · игра {game}" + (mode != null ? $" ({mode.Phase}, {mode.TimeLeft:0} с)" : "") : "") +
                  $" · игроков {NetworkPlayer.All.Count} · {(IsHost ? "ты хост" : "ты клиент")}", Kind.Info);
        }

        private static string PhaseName(SessionPhase phase) => phase switch
        {
            SessionPhase.Hub => "двор (голосование)",
            SessionPhase.MiniGame => "идёт мини-игра",
            SessionPhase.Results => "итоги мини-игры",
            SessionPhase.Auction => "аукцион за зону",
            SessionPhase.Final => "финал партии",
            _ => phase.ToString()
        };

        private static void Players()
        {
            var players = NetworkPlayer.All;
            if (players.Count == 0) { Print("Игроков нет — ты не в игре.", Kind.Warn); return; }
            var session = SessionManager.Instance;
            Print("Игроки:", Kind.Ok);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                int zones = session != null ? session.ZoneCount(p.OwnerClientId) : 0;
                string me = p == NetworkPlayer.Local ? " (ты)" : "";
                string host = p.IsHostPlayer ? " · хост" : "";
                Print($"  {i + 1}. {p.DisplayName}{me}{host} · рыба {p.Fish.Value} · зон {zones}", Kind.Info);
            }
        }

        /// <summary>Игрок по номеру из players, по началу имени, «all» — все; null/пусто — свой.</summary>
        private static List<NetworkPlayer> Targets(string query)
        {
            var players = NetworkPlayer.All;
            if (string.IsNullOrEmpty(query))
                return NetworkPlayer.Local != null ? new List<NetworkPlayer> { NetworkPlayer.Local } : new List<NetworkPlayer>();
            if (query.Equals("all", StringComparison.OrdinalIgnoreCase) || query == "все")
                return players.ToList();
            if (int.TryParse(query, out int n) && n >= 1 && n <= players.Count)
                return new List<NetworkPlayer> { players[n - 1] };
            var exact = players.Where(p => p.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            return players.Where(p => p.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<NetworkPlayer> OneTarget(string[] args, int from)
        {
            string query = args.Length > from ? string.Join(" ", args.Skip(from)) : null;
            var targets = Targets(query);
            if (targets.Count == 0) Print(query == null ? "Ты не в игре." : $"Игрок «{query}» не найден. Список — players.", Kind.Error);
            else if (targets.Count > 1 && !(query ?? "").Equals("all", StringComparison.OrdinalIgnoreCase) && query != "все")
            {
                Print($"Под «{query}» подходят несколько игроков: {string.Join(", ", targets.Select(t => t.DisplayName))}. Укажи номер.", Kind.Warn);
                return new List<NetworkPlayer>();
            }
            return targets;
        }

        private static void Fish(string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                Print("Как: fish <число> [игрок|all]. Например: fish 20, fish 15 2, fish -5 Барсик.", Kind.Warn);
                return;
            }

            foreach (var player in OneTarget(args, 1))
            {
                player.AddFish(amount);
                Print($"{player.DisplayName}: {(amount >= 0 ? "+" : "")}{amount} рыб → {player.Fish.Value}", Kind.Ok);
            }
        }

        private static void Stun(string[] args)
        {
            foreach (var player in OneTarget(args, 0))
            {
                var cat = CatController.FindByOwner(player.OwnerClientId);
                if (cat == null) { Print($"У {player.DisplayName} сейчас нет кота.", Kind.Warn); continue; }
                cat.ReceiveHitRpc(Vector3.up * 2f, true);
                Print($"{player.DisplayName} оглушён.", Kind.Ok);
            }
        }

        private static void Teleport(string[] args)
        {
            if (args.Length == 0) { Print("Как: tp <игрок>.", Kind.Warn); return; }
            var targets = OneTarget(args, 0);
            if (targets.Count != 1) return;
            var me = NetworkPlayer.Local != null ? CatController.FindByOwner(NetworkPlayer.Local.OwnerClientId) : null;
            var other = CatController.FindByOwner(targets[0].OwnerClientId);
            if (me == null || other == null) { Print("Нужны оба кота на сцене.", Kind.Error); return; }
            if (me == other) { Print("Это ты.", Kind.Warn); return; }
            Vector3 to = other.transform.position - other.transform.forward * 1.2f + Vector3.up * 0.3f;
            me.SetRespawnAndTeleportRpc(to, other.transform.rotation);
            Print($"Ты у кота {targets[0].DisplayName}.", Kind.Ok);
        }

        private static void Cans(string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int amount)) { Print("Как: cans <число>.", Kind.Warn); return; }
            Cosmetics.DebugAdd(amount);
            Print($"Банок: {Cosmetics.Cans}.", Kind.Ok);
        }

        private static void Quality(string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int preset) || preset < 0 || preset >= GameSettings.PresetNames.Length)
            {
                Print($"Как: quality <0-3>. Сейчас: {GameSettings.PresetName(GameSettings.CurrentPreset)}.", Kind.Warn);
                return;
            }
            GameSettings.ApplyPreset(preset);
            GameSettings.ApplyAndSave();
            Print($"Общее качество: {GameSettings.PresetNames[preset]}.", Kind.Ok);
        }

        private static void Toggle(string what, Action flip, Func<bool> value)
        {
            flip();
            GameSettings.ApplyAndSave();
            Print($"{what}: {(value() ? "вкл" : "выкл")}.", Kind.Ok);
        }
    }
}
