using System;
using System.Collections.Generic;
using Maoshi.Audio;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Maoshi.MiniGames.WhoBrokeVase
{
    /// <summary>Голос игрока: кого он считает виноватым.</summary>
    public struct VaseVote : INetworkSerializable, IEquatable<VaseVote>
    {
        public ulong Voter;
        public ulong Target;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Voter);
            serializer.SerializeValue(ref Target);
        }

        public bool Equals(VaseVote other) => Voter == other.Voter && Target == other.Target;
    }

    /// <summary>
    /// «Кто опрокинул вазу»: обман и общение.
    ///
    /// 1. Пакость (50 с). Один случайный кот — тайный пакостник (знает только он). Толчок лапой (F) по вазе у всех выглядит
    ///    одинаково — ваза качается. Но от лапы пакостника она через 2–3 с упадёт и разобьётся, если её не поймать:
    ///    любой другой кот, толкнувший падающую вазу, спасает её (+2). Ещё по квартире гуляет сквозняк — тоже роняет вазы.
    ///    При 3+ игроках пакостник есть всегда; при двоих — с шансом 50%, иначе вазы роняет только сквозняк.
    /// 2. «Кто опрокинул вазу?!» (20 с). Все голосуют: кто-то из котов или «Сквозняк».
    /// 3. Разоблачение (7 с). Пакостник пойман, если за него проголосовало больше всего (ничья — ушёл).
    ///
    /// Очки (видны только в конце): пакостник +2 за каждую свою разбитую вазу и +5, если ушёл;
    /// остальные +4 за верный голос и +2 за каждую спасённую вазу. Места — по очкам.
    /// </summary>
    public class VaseMode : MiniGameMode, IMiniGameModal
    {
        public enum Stage : byte { Mischief, Vote, Reveal }
        public enum Role : byte { Unknown, Saboteur, Innocent }

        /// <summary>«Виновник» — сквозняк.</summary>
        public const ulong Draft = ulong.MaxValue - 1;
        public const ulong NoOne = ulong.MaxValue;

        [Header("Кто опрокинул вазу")]
        [SerializeField] private Vase[] vases;
        [SerializeField] private float mischiefSeconds = 50f;
        [SerializeField] private float voteSeconds = 20f;
        [SerializeField] private float revealSeconds = 7f;
        [SerializeField] private float saboteurCooldown = 5f;
        [SerializeField] private Vector2 fallDelay = new(1.8f, 2.8f);

        public readonly NetworkVariable<Stage> GameStage = new(Stage.Mischief);
        public readonly NetworkVariable<int> BrokenCount = new();
        public readonly NetworkVariable<int> SavedCount = new();
        /// <summary>Открывается только на разоблачении: кто был пакостником (Draft — никого, вазы ронял сквозняк).</summary>
        public readonly NetworkVariable<ulong> RevealedCulprit = new(NoOne);
        public readonly NetworkVariable<bool> RevealedCaught = new();
        public readonly NetworkVariable<int> RevealedBySaboteur = new();
        public NetworkList<VaseVote> Votes;

        /// <summary>Роль этого игрока (сообщается только ему).</summary>
        public Role LocalRole { get; private set; } = Role.Unknown;

        // Сервер.
        private ulong _saboteur = NoOne;
        private readonly Dictionary<ulong, int> _points = new();
        private readonly List<double> _draftTimes = new();
        private double _stageEnd;
        private double _saboteurReadyAt;
        private int _brokenBySaboteur;
        private double _roleShownUntil;

        protected override bool UsesScores => false;

        protected override void Awake()
        {
            base.Awake();
            Votes = new NetworkList<VaseVote>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && vases != null) foreach (var vase in vases) if (vase != null) vase.Mode = this;
        }

        // ───────────────────────── Сервер: ход игры ─────────────────────────

        protected override void OnPlayersReady()
        {
            _points.Clear();
            foreach (var id in Participants) _points[id] = 0;

            bool hasSaboteur = Participants.Count >= 3 || Participants.Count == 1 || (Participants.Count == 2 && Random.value < 0.5f);
            _saboteur = hasSaboteur && Participants.Count > 0 ? Participants[Random.Range(0, Participants.Count)] : NoOne;
            _brokenBySaboteur = 0;
        }

        protected override void OnGameStarted()
        {
            SetStage(Stage.Mischief, mischiefSeconds);
            // Роль — лично каждому (остальные не узнают до разоблачения).
            foreach (var id in Participants)
                TellRoleRpc(id == _saboteur, RpcTarget.Single(id, RpcTargetUse.Temp));
            _saboteurReadyAt = Now + 2.5;

            // Сквозняк: без пакостника — чаще, с ним — разок, для путаницы.
            _draftTimes.Clear();
            int drafts = _saboteur == NoOne ? 4 : 1;
            for (int i = 0; i < drafts; i++) _draftTimes.Add(Now + Random.Range(8f, mischiefSeconds - 6f));
            _draftTimes.Sort();
        }

        protected override void OnGameTick()
        {
            // Падающие вазы.
            foreach (var vase in vases)
            {
                if (vase == null) continue;
                if (vase.State.Value == Vase.VaseState.Doomed && vase.StateAge >= vase.FallDelay.Value) BreakVase(vase);
                else if (vase.State.Value == Vase.VaseState.Wobble && vase.StateAge > 1.1f) vase.ServerSettle();
            }

            switch (GameStage.Value)
            {
                case Stage.Mischief:
                    while (_draftTimes.Count > 0 && Now >= _draftTimes[0])
                    {
                        _draftTimes.RemoveAt(0);
                        DraftKnocks();
                    }
                    if (Now >= _stageEnd || StandingCount() == 0) StartVote();
                    break;

                case Stage.Vote:
                    if (Now >= _stageEnd || Votes.Count >= Participants.Count) StartReveal();
                    break;

                case Stage.Reveal:
                    if (Now >= _stageEnd) FinishGame();
                    break;
            }
        }

        private void SetStage(Stage stage, float seconds)
        {
            _stageEnd = Now + seconds;
            PhaseEndTime.Value = _stageEnd;
            GameStage.Value = stage;
        }

        private void StartVote()
        {
            // Падающие вазы падают сразу — чтобы голосовали по честному итогу.
            foreach (var vase in vases)
                if (vase != null && vase.State.Value == Vase.VaseState.Doomed) BreakVase(vase);
            Votes.Clear();
            SetStage(Stage.Vote, voteSeconds);
            AnnounceRpc();
        }

        private void StartReveal()
        {
            ulong truth = _saboteur == NoOne ? Draft : _saboteur;

            // Подсчёт голосов: больше всех — «обвинённый»; ничья — никого.
            var tally = new Dictionary<ulong, int>();
            foreach (var vote in Votes) tally[vote.Target] = tally.TryGetValue(vote.Target, out int n) ? n + 1 : 1;
            ulong accused = NoOne;
            int best = 0;
            bool tie = false;
            foreach (var pair in tally)
            {
                if (pair.Value > best) { best = pair.Value; accused = pair.Key; tie = false; }
                else if (pair.Value == best) tie = true;
            }
            if (tie) accused = NoOne;

            bool caught = _saboteur != NoOne && accused == _saboteur;
            if (_saboteur != NoOne)
            {
                Add(_saboteur, _brokenBySaboteur * 2);
                if (!caught) Add(_saboteur, 5);
            }
            foreach (var vote in Votes)
                if (vote.Voter != _saboteur && vote.Target == truth) Add(vote.Voter, 4);

            RevealedCulprit.Value = truth;
            RevealedCaught.Value = caught || (_saboteur == NoOne && accused == Draft);
            RevealedBySaboteur.Value = _brokenBySaboteur;

            // Таблица очков появляется только сейчас.
            Scores.Clear();
            foreach (var id in Participants) Scores.Add(new PlayerScore { ClientId = id, Score = _points.TryGetValue(id, out int p) ? p : 0 });

            SetStage(Stage.Reveal, revealSeconds);
        }

        protected override List<Placement> BuildPlacements() => PlacementsByScore();

        private void Add(ulong id, int amount)
        {
            if (!_points.ContainsKey(id)) _points[id] = 0;
            _points[id] += amount;
        }

        // ───────────────────────── Сервер: вазы ─────────────────────────

        /// <summary>Кот толкнул вазу лапой.</summary>
        public void OnVaseNudged(Vase vase, ulong clientId, Vector3 direction)
        {
            if (!IsServer || Phase != MiniGamePhase.Playing || GameStage.Value != Stage.Mischief) return;

            switch (vase.State.Value)
            {
                case Vase.VaseState.Doomed:
                    // Поймал падающую! Свою же пакостник «ловить» не может.
                    if (clientId == vase.Culprit) return;
                    Add(clientId, 2);
                    SavedCount.Value++;
                    vase.ServerSave();
                    break;

                case Vase.VaseState.Standing:
                case Vase.VaseState.Wobble:
                    if (clientId == _saboteur && Now >= _saboteurReadyAt && !AnyDoomed())
                    {
                        _saboteurReadyAt = Now + saboteurCooldown;
                        vase.ServerDoom(clientId, Random.Range(fallDelay.x, fallDelay.y));
                    }
                    else vase.ServerWobble();
                    break;
            }
        }

        private void DraftKnocks()
        {
            var candidates = new List<Vase>();
            foreach (var vase in vases)
                if (vase != null && vase.State.Value == Vase.VaseState.Standing) candidates.Add(vase);
            if (candidates.Count == 0) return;
            var target = candidates[Random.Range(0, candidates.Count)];
            target.ServerDoom(Draft, Random.Range(fallDelay.x + 0.3f, fallDelay.y + 0.5f));
            WindRpc(target.transform.position);
        }

        private void BreakVase(Vase vase)
        {
            if (vase.Culprit == _saboteur && _saboteur != NoOne) _brokenBySaboteur++;
            vase.ServerBreak();
            BrokenCount.Value++;
        }

        private bool AnyDoomed()
        {
            foreach (var vase in vases) if (vase != null && vase.State.Value == Vase.VaseState.Doomed) return true;
            return false;
        }

        private int StandingCount()
        {
            int n = 0;
            foreach (var vase in vases) if (vase != null && vase.State.Value != Vase.VaseState.Broken) n++;
            return n;
        }

        public int TotalVases => vases != null ? vases.Length : 0;

        // ───────────────────────── Сеть ─────────────────────────

        [Rpc(SendTo.SpecifiedInParams)]
        private void TellRoleRpc(bool saboteur, RpcParams rpcParams)
        {
            LocalRole = saboteur ? Role.Saboteur : Role.Innocent;
            _roleShownUntil = Time.unscaledTimeAsDouble + 6;
        }

        [Rpc(SendTo.Everyone)]
        private void AnnounceRpc() => AudioManager.Play("announce");

        [Rpc(SendTo.Everyone)]
        private void WindRpc(Vector3 at)
        {
            AudioManager.Play("wind", 0.8f);
            AudioManager.PlayAt("clink", at, 0.5f, 0.1f, 20f);
        }

        /// <summary>Голос с экрана: target — id кота или Draft.</summary>
        public void Vote(ulong target) => VoteRpc(target);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void VoteRpc(ulong target, RpcParams rpcParams = default)
        {
            ulong voter = rpcParams.Receive.SenderClientId;
            if (GameStage.Value != Stage.Vote || !Participants.Contains(voter) || target == voter) return;
            if (target != Draft && !Participants.Contains(target)) return;
            for (int i = 0; i < Votes.Count; i++)
                if (Votes[i].Voter == voter) { Votes[i] = new VaseVote { Voter = voter, Target = target }; return; }
            Votes.Add(new VaseVote { Voter = voter, Target = target });
        }

        // ───────────────────────── Подсказки и окно ─────────────────────────

        private ulong LocalId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        public override string StatusText
        {
            get
            {
                string vasesInfo = $"разбито {BrokenCount.Value} из {TotalVases}, спасено {SavedCount.Value}";
                switch (GameStage.Value)
                {
                    case Stage.Mischief:
                        return LocalRole == Role.Saboteur
                            ? $"Ты пакостник: толкни вазу (F) и отойди, пока не видят. {vasesInfo}"
                            : $"Лови качающиеся вазы (F) и запоминай, кто был рядом. {vasesInfo}";
                    case Stage.Vote:
                        return $"Бабушка проснулась! Кто опрокинул вазу? {vasesInfo}";
                    default:
                        return vasesInfo;
                }
            }
        }

        public string ModalKey
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "";
                switch (GameStage.Value)
                {
                    case Stage.Mischief:
                        return LocalRole != Role.Unknown && Time.unscaledTimeAsDouble < _roleShownUntil ? "vase-role:" + LocalRole : "";
                    case Stage.Vote:
                        var key = new System.Text.StringBuilder("vase-vote:").Append(Mathf.CeilToInt(TimeLeft)).Append(':').Append(Votes.Count).Append(':');
                        foreach (var vote in Votes) if (vote.Voter == LocalId) key.Append(vote.Target);
                        return key.ToString();
                    case Stage.Reveal:
                        return "vase-reveal:" + RevealedCulprit.Value + RevealedCaught.Value + Votes.Count;
                }
                return "";
            }
        }

        public VisualElement BuildModal()
        {
            return GameStage.Value switch
            {
                Stage.Mischief => BuildRoleCard(),
                Stage.Vote => BuildVote(),
                _ => BuildReveal()
            };
        }

        private VisualElement BuildRoleCard()
        {
            var panel = UIKit.Box("panel", "panel--narrow");
            panel.pickingMode = PickingMode.Ignore;
            bool saboteur = LocalRole == Role.Saboteur;
            panel.Add(UIKit.Text(saboteur ? "Ты — пакостник" : "Ты — тихоня", "h1"));
            panel.Add(UIKit.Text(saboteur
                ? "Толкни вазу лапой (F) — через пару секунд она упадёт. Отойди, чтобы не заподозрили. Твою вазу может поймать любой другой кот."
                : "Кто-то из котов тайно роняет бабушкины вазы. Лови качающиеся вазы лапой (F) и запоминай, кто крутился рядом. Ещё здесь гуляет сквозняк.", "text"));
            panel.Add(UIKit.Text(saboteur ? "никому не говори" : "смотри в оба", "chalk"));
            return panel;
        }

        private VisualElement BuildVote()
        {
            var panel = UIKit.Box("panel");
            panel.Add(UIKit.Text("Кто опрокинул вазу?!", "h1"));
            panel.Add(UIKit.Text($"Бабушка проснулась. Разбито ваз: {BrokenCount.Value} из {TotalVases}. Осталось {Mathf.CeilToInt(TimeLeft)} c.", "muted"));

            ulong mine = NoOne;
            foreach (var vote in Votes) if (vote.Voter == LocalId) mine = vote.Target;

            var grid = UIKit.Layout("vote-grid");
            foreach (var player in NetworkPlayer.All)
            {
                if (player.OwnerClientId == LocalId || !Participants_Contains(player.OwnerClientId)) continue;
                ulong id = player.OwnerClientId;
                var button = UIKit.Button("", () => Vote(id), "vote-option");
                button.Add(UIKit.Swatch(player.Color));
                var label = UIKit.Text(player.DisplayName, "vote-option__name");
                label.pickingMode = PickingMode.Ignore;
                button.Add(label);
                if (mine == id) button.AddToClassList("vote-option--chosen");
                grid.Add(button);
            }
            var draft = UIKit.Button("", () => Vote(Draft), "vote-option", "vote-option--draft");
            var draftLabel = UIKit.Text("Сквозняк", "vote-option__name");
            draftLabel.pickingMode = PickingMode.Ignore;
            draft.Add(draftLabel);
            if (mine == Draft) draft.AddToClassList("vote-option--chosen");
            grid.Add(draft);
            panel.Add(grid);

            int total = NetworkPlayer.All.Count;
            panel.Add(UIKit.Text(mine == NoOne ? "Выбери, на кого думаешь. Можно передумать до конца голосования." : $"Голос принят. Проголосовали {Votes.Count} из {total}.", "muted"));
            return panel;
        }

        /// <summary>Клиенты не знают серверный список участников — считаем участниками всех, кто есть в игре.</summary>
        private bool Participants_Contains(ulong id) => AlivePlayers.Contains(id);

        private VisualElement BuildReveal()
        {
            var panel = UIKit.Box("panel");
            ulong culprit = RevealedCulprit.Value;
            string name = culprit == Draft ? "сквозняк" : NameOf(culprit);
            panel.Add(UIKit.Text(culprit == Draft ? "Это был сквозняк!" : $"Вазы ронял {name}!", "h1"));
            string verdict = culprit == Draft
                ? (RevealedCaught.Value ? "Коты не дали себя обмануть." : "Коты подозревали друг друга зря.")
                : (RevealedCaught.Value ? "Пакостник пойман." : "Пакостник ушёл безнаказанным.");
            panel.Add(UIKit.Text(verdict + (culprit != Draft ? $" Разбито им ваз: {RevealedBySaboteur.Value}." : ""), "text"));
            panel.Add(UIKit.Spacer(8));
            foreach (var vote in Votes)
            {
                var row = UIKit.Layout("player-row");
                var voter = NetworkPlayer.FindByClientId(vote.Voter);
                if (voter != null) row.Add(UIKit.Swatch(voter.Color));
                string target = vote.Target == Draft ? "сквозняк" : NameOf(vote.Target);
                bool right = vote.Target == culprit;
                bool isCulprit = vote.Voter == culprit;
                row.Add(UIKit.Text(isCulprit ? $"{NameOf(vote.Voter)} валил на: {target}" : $"{NameOf(vote.Voter)} думал на: {target}", "text", "grow"));
                row.Add(isCulprit ? UIKit.Text("хитрил", "tag") : UIKit.Text(right ? "верно" : "мимо", "tag", right ? "tag--ready" : "tag--waiting"));
                panel.Add(row);
            }
            if (Votes.Count == 0) panel.Add(UIKit.Text("Никто не проголосовал.", "muted"));
            return panel;
        }

        private static string NameOf(ulong id)
        {
            var player = NetworkPlayer.FindByClientId(id);
            return player != null ? player.DisplayName : "кто-то";
        }
    }
}
