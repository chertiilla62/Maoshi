using System;
using System.Collections.Generic;
using System.Text;
using Maoshi.Audio;
using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Maoshi.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Maoshi.MiniGames.SourCream
{
    /// <summary>Голос: кого игрок считает тем, кто ел сметану (NoOne — «никто не ел»).</summary>
    public struct CreamVote : INetworkSerializable, IEquatable<CreamVote>
    {
        public ulong Voter;
        public ulong Target;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Voter);
            serializer.SerializeValue(ref Target);
        }

        public bool Equals(CreamVote other) => Voter == other.Voter && Target == other.Target;
    }

    /// <summary>
    /// «Кто съел сметану?» — обман и подстава. 3 раунда, в каждом:
    /// 1. Хозяйка ушла (22 с). В кладовке стоит миска сметаны. Подойди и нажми E — лизнул (+2 очка, до 2 раз
    ///    за раунд), но усы в сметане. Никто, кроме тебя, этого не знает. Толкни другого кота грязной лапой —
    ///    измажешь ему усы (он об этом не узнает!). Постой 1.5 с на половике у двери — умылся (усы и лапа чистые).
    /// 2. Хозяйка вернулась (5 с): у кого усы в сметане — видно всем, и сколько ложек пропало.
    /// 3. «Кто съел сметану?!» (14 с): все голосуют за кота или «никто».
    /// 4. Приговор (6 с): обвинённый (больше всех голосов, ничья — никто). Если он правда ел — −3,
    ///    если нет — его подставщики +3. Угадавшие (голос за того, кто ел; или «никто», если не ели) +2.
    ///    Очки за лизания (+2) засчитываются тоже здесь — до приговора таблица не меняется.
    /// Секреты (кто лизал, кто кого измазал) — только на сервере; каждому лично приходят его события.
    /// </summary>
    public class SourCreamMode : MiniGameMode, IMiniGameModal
    {
        public static SourCreamMode Instance { get; private set; }

        public enum Stage : byte { Prowl, Reveal, Vote, Verdict }
        public const ulong NoOne = ulong.MaxValue;

        [SerializeField] private int rounds = 3;
        [SerializeField] private float prowlSeconds = 22f;
        [SerializeField] private float revealSeconds = 5f;
        [SerializeField] private float voteSeconds = 14f;
        [SerializeField] private float verdictSeconds = 6f;
        [SerializeField] private int maxLicksPerRound = 2;
        [SerializeField] private Transform bowl;
        [SerializeField] private float lickRange = 1.4f;
        [SerializeField] private Transform rug;
        [SerializeField] private Vector2 rugHalfSize = new(0.8f, 0.55f);
        [SerializeField] private float washSeconds = 1.5f;

        public readonly NetworkVariable<Stage> GameStage = new(Stage.Prowl);
        public readonly NetworkVariable<int> Round = new();
        /// <summary>Сколько ложек пропало в этом раунде (открывается, когда хозяйка вернулась).</summary>
        public readonly NetworkVariable<int> RevealedLicks = new();
        /// <summary>Сколько всего съедено за игру (уровень сметаны в миске).</summary>
        public readonly NetworkVariable<int> EatenTotal = new();
        public readonly NetworkVariable<ulong> Accused = new(NoOne);
        public readonly NetworkVariable<bool> AccusedAte = new();
        /// <summary>Большинство решило «никто не ел».</summary>
        public readonly NetworkVariable<bool> VotedNobody = new();
        public NetworkList<ulong> SmearedShown;
        public NetworkList<CreamVote> Votes;
        /// <summary>Кто сколько лизнул в раунде — открывается на приговоре (для окна итогов раунда).</summary>
        public NetworkList<ulong> RevealedEaters;

        // Сервер: секреты раунда.
        private readonly Dictionary<ulong, int> _licks = new();
        private readonly HashSet<ulong> _smeared = new();
        private readonly HashSet<ulong> _dirtyPaw = new();
        private readonly Dictionary<ulong, List<ulong>> _framedBy = new();
        private readonly Dictionary<ulong, float> _onRug = new();
        private readonly Dictionary<ulong, double> _nextLick = new();
        private double _stageEnd;

        // Клиент: свои события.
        public string LocalNote { get; private set; } = "";
        private double _noteUntil;
        public bool LocalKnowsSmeared { get; private set; }
        public int LocalLicks { get; private set; }

        protected override bool UsesScores => true;

        protected override void Awake()
        {
            base.Awake();
            SmearedShown = new NetworkList<ulong>();
            Votes = new NetworkList<CreamVote>();
            RevealedEaters = new NetworkList<ulong>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            CatVoice.Suppressed = true; // E — лизнуть, а не мяукнуть
            GameStage.OnValueChanged += OnStageChanged;
            if (IsServer) CatController.ServerCatPushed += OnCatPushed;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            CatVoice.Suppressed = false;
            GameStage.OnValueChanged -= OnStageChanged;
            CatController.ServerCatPushed -= OnCatPushed;
            base.OnNetworkDespawn();
        }

        public Vector3 BowlPosition => bowl != null ? bowl.position : Vector3.zero;

        // ───────────────────────── Сервер: раунды ─────────────────────────

        protected override void OnGameStarted() => StartRound();

        private void SetStage(Stage stage, float seconds)
        {
            _stageEnd = Now + seconds;
            PhaseEndTime.Value = _stageEnd;
            GameStage.Value = stage;
        }

        private void StartRound()
        {
            _licks.Clear();
            _smeared.Clear();
            _dirtyPaw.Clear();
            _framedBy.Clear();
            _onRug.Clear();
            SmearedShown.Clear();
            Votes.Clear();
            RevealedEaters.Clear();
            Accused.Value = NoOne;
            RevealedLicks.Value = 0;
            if (Round.Value > 0 && Maoshi.Gameplay.CatSpawner.Current != null) Maoshi.Gameplay.CatSpawner.Current.ReturnAllToSpawn();
            foreach (ulong id in AlivePlayers) ResetLocalRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            SetStage(Stage.Prowl, prowlSeconds);
            DoorRpc(false);
        }

        protected override void OnGameTick()
        {
            switch (GameStage.Value)
            {
                case Stage.Prowl:
                    TickWashing();
                    if (Now >= _stageEnd) StartReveal();
                    break;
                case Stage.Reveal:
                    if (Now >= _stageEnd) SetStage(Stage.Vote, voteSeconds);
                    break;
                case Stage.Vote:
                    if (Now >= _stageEnd || Votes.Count >= AlivePlayers.Count) StartVerdict();
                    break;
                case Stage.Verdict:
                    if (Now >= _stageEnd)
                    {
                        Round.Value++;
                        if (Round.Value >= rounds) FinishGame();
                        else StartRound();
                    }
                    break;
            }
        }

        private void TickWashing()
        {
            if (rug == null) return;
            float dt = Time.deltaTime;
            foreach (ulong id in AlivePlayers)
            {
                var cat = CatController.FindByOwner(id);
                if (cat == null) continue;
                Vector3 local = rug.InverseTransformPoint(cat.transform.position);
                bool on = Mathf.Abs(local.x) <= rugHalfSize.x && Mathf.Abs(local.z) <= rugHalfSize.y && Mathf.Abs(local.y) < 0.8f;
                _onRug.TryGetValue(id, out float time);
                time = on ? time + dt : 0f;
                if (time >= washSeconds && (_smeared.Contains(id) || _dirtyPaw.Contains(id)))
                {
                    _smeared.Remove(id);
                    _dirtyPaw.Remove(id);
                    TellRpc("Умылся: усы и лапы чистые", false, -1, RpcTarget.Single(id, RpcTargetUse.Temp));
                }
                else if (time >= washSeconds && time - dt < washSeconds)
                {
                    TellRpc("Ты и так чистый", false, -1, RpcTarget.Single(id, RpcTargetUse.Temp));
                }
                _onRug[id] = time;
            }
        }

        private void StartReveal()
        {
            int total = 0;
            foreach (var pair in _licks) total += pair.Value;
            RevealedLicks.Value = total;
            EatenTotal.Value += total;
            foreach (ulong id in AlivePlayers) if (_smeared.Contains(id)) SmearedShown.Add(id);
            SetStage(Stage.Reveal, revealSeconds);
            DoorRpc(true);
        }

        private void StartVerdict()
        {
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
            bool votedNobody = !tie && accused == NoOne && best > 0;
            if (tie) accused = NoOne;

            bool anyAte = RevealedLicks.Value > 0;
            foreach (var pair in _licks)
            {
                if (pair.Value <= 0) continue;
                AddScore(pair.Key, pair.Value * 2);
                RevealedEaters.Add(pair.Key);
            }
            bool accusedAte = accused != NoOne && _licks.TryGetValue(accused, out int licks) && licks > 0;
            if (accused != NoOne)
            {
                if (accusedAte) AddScore(accused, -3);
                else if (_framedBy.TryGetValue(accused, out var framers))
                    foreach (ulong framer in framers) AddScore(framer, 3);
            }
            foreach (var vote in Votes)
            {
                bool right = vote.Target == NoOne ? !anyAte : _licks.TryGetValue(vote.Target, out int l) && l > 0;
                if (right && vote.Target != vote.Voter) AddScore(vote.Voter, 2);
            }

            Accused.Value = accused;
            AccusedAte.Value = accusedAte;
            VotedNobody.Value = votedNobody;
            SetStage(Stage.Verdict, verdictSeconds);
            VerdictSoundRpc();
        }

        // ───────────────────────── Сервер: действия ─────────────────────────

        public void Lick() => LickRpc();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void LickRpc(RpcParams rpcParams = default)
        {
            ulong id = rpcParams.Receive.SenderClientId;
            if (Phase != MiniGamePhase.Playing || GameStage.Value != Stage.Prowl || !IsAlive(id)) return;
            var cat = CatController.FindByOwner(id);
            if (cat == null || bowl == null) return;
            Vector3 offset = cat.transform.position - bowl.position;
            offset.y = 0f;
            if (offset.magnitude > lickRange + 0.6f) return;
            if (_nextLick.TryGetValue(id, out double next) && Now < next) return;
            _licks.TryGetValue(id, out int licks);
            if (licks >= maxLicksPerRound)
            {
                TellRpc("Больше не лезет — уже 2 ложки за раунд", false, licks, RpcTarget.Single(id, RpcTargetUse.Temp));
                return;
            }
            _nextLick[id] = Now + 1.0;
            _licks[id] = licks + 1;
            _smeared.Add(id);
            _dirtyPaw.Add(id);
            TellRpc($"Лизнул! +2 (ложек: {licks + 1}). Усы в сметане — умойся на половике или измажь кого-нибудь лапой", true, licks + 1,
                RpcTarget.Single(id, RpcTargetUse.Temp));
        }

        private void OnCatPushed(CatController attacker, CatController victim)
        {
            if (Phase != MiniGamePhase.Playing || GameStage.Value != Stage.Prowl) return;
            ulong from = attacker.OwnerClientId, to = victim.OwnerClientId;
            if (!_dirtyPaw.Contains(from)) return;
            _dirtyPaw.Remove(from);
            _smeared.Add(to);
            if (!_framedBy.TryGetValue(to, out var list)) _framedBy[to] = list = new List<ulong>();
            if (!list.Contains(from)) list.Add(from);
            TellRpc($"Измазал сметаной усы: {NameOf(to)}. Он не знает!", false, -1, RpcTarget.Single(from, RpcTargetUse.Temp));
        }

        public void Vote(ulong target) => VoteRpc(target);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void VoteRpc(ulong target, RpcParams rpcParams = default)
        {
            ulong voter = rpcParams.Receive.SenderClientId;
            if (GameStage.Value != Stage.Vote || !IsAlive(voter) || target == voter) return;
            if (target != NoOne && !IsAlive(target)) return;
            for (int i = 0; i < Votes.Count; i++)
                if (Votes[i].Voter == voter) { Votes[i] = new CreamVote { Voter = voter, Target = target }; return; }
            Votes.Add(new CreamVote { Voter = voter, Target = target });
        }

        // ───────────────────────── Клиенты ─────────────────────────

        [Rpc(SendTo.SpecifiedInParams)]
        private void TellRpc(string text, bool licked, int licks, RpcParams rpcParams)
        {
            LocalNote = text;
            _noteUntil = Time.unscaledTimeAsDouble + 3.5;
            if (licked)
            {
                LocalKnowsSmeared = true;
                LocalLicks = licks;
                var me = CatController.FindByOwner(NetworkManager.LocalClientId);
                AudioManager.PlayAt("lick", me != null ? me.transform.position : BowlPosition, 0.9f);
            }
            else if (text.StartsWith("Умылся"))
            {
                LocalKnowsSmeared = false;
                AudioManager.Play("splash", 0.4f);
            }
            else if (text.StartsWith("Измазал"))
            {
                AudioManager.Play("pass", 0.6f);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ResetLocalRpc(RpcParams rpcParams)
        {
            LocalKnowsSmeared = false;
            LocalLicks = 0;
            LocalNote = "";
        }

        [Rpc(SendTo.Everyone)]
        private void DoorRpc(bool open) => AudioManager.Play(open ? "announce" : "ui_back", 0.6f);

        [Rpc(SendTo.Everyone)]
        private void VerdictSoundRpc() => AudioManager.Play("suspense", 0.7f);

        private void OnStageChanged(Stage previous, Stage next)
        {
            if (next == Stage.Prowl) { LocalKnowsSmeared = false; LocalLicks = 0; }
        }

        /// <summary>Свой кот у миски (для подсказки и нажатия E).</summary>
        public bool LocalNearBowl
        {
            get
            {
                var nm = NetworkManager.Singleton;
                var me = nm != null ? CatController.FindByOwner(nm.LocalClientId) : null;
                if (me == null || bowl == null) return false;
                Vector3 offset = me.transform.position - bowl.position;
                offset.y = 0f;
                return offset.magnitude <= lickRange;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsSpawned || Phase != MiniGamePhase.Playing || GameStage.Value != Stage.Prowl) return;
            var keyboard = Maoshi.GameKeyboard.Current;
            if (keyboard != null && !CatInput.Blocked && keyboard.eKey.wasPressedThisFrame && LocalNearBowl) Lick();
        }

        public override string StatusText
        {
            get
            {
                if (Phase != MiniGamePhase.Playing) return "Хозяйка ушла в магазин… Кто полезет в сметану?";
                string round = $"Раунд {Mathf.Min(Round.Value + 1, rounds)} из {rounds}. ";
                bool note = Time.unscaledTimeAsDouble < _noteUntil && !string.IsNullOrEmpty(LocalNote);
                switch (GameStage.Value)
                {
                    case Stage.Prowl:
                        if (note) return round + LocalNote;
                        if (LocalNearBowl) return round + "Миска сметаны: E — лизнуть (+2, но усы испачкаешь)";
                        return round + (LocalKnowsSmeared
                            ? "Усы в сметане! Умойся на половике у двери или измажь кого-нибудь толчком"
                            : "Сметана в кладовке (E — лизнуть). Толчок грязной лапой пачкает чужие усы. Половик — умыться");
                    case Stage.Reveal:
                        return round + $"Хозяйка вернулась! Пропало ложек: {RevealedLicks.Value}. Смотрите на усы…";
                    case Stage.Vote:
                        return round + "Кто съел сметану? Голосуй!";
                    default:
                        return round + (Accused.Value == NoOne ? (VotedNobody.Value ? "Решили, что никто не ел" : "Никого не наказали") : $"Наказан: {NameOf(Accused.Value)}");
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
                    case Stage.Vote:
                        var key = new StringBuilder("cream-vote:").Append(Round.Value).Append(':').Append(Mathf.CeilToInt(TimeLeft)).Append(':').Append(Votes.Count).Append(':');
                        foreach (var vote in Votes) if (vote.Voter == LocalId) key.Append(vote.Target);
                        return key.ToString();
                    case Stage.Verdict:
                        return "cream-verdict:" + Round.Value + Accused.Value + AccusedAte.Value + VotedNobody.Value + RevealedEaters.Count + Votes.Count;
                }
                return "";
            }
        }

        private ulong LocalId => NetworkManager != null ? NetworkManager.LocalClientId : 0;

        public VisualElement BuildModal() => GameStage.Value == Stage.Vote ? BuildVote() : BuildVerdict();

        private VisualElement BuildVote()
        {
            var panel = UIKit.Box("panel");
            panel.Add(UIKit.Text("Кто съел сметану?!", "h1"));
            int smeared = SmearedShown.Count;
            panel.Add(UIKit.Text($"Пропало ложек: {RevealedLicks.Value}. Усы в сметане у {smeared} {Plural(smeared, "кота", "котов", "котов")}. Осталось {Mathf.CeilToInt(TimeLeft)} c.", "muted"));

            ulong mine = ulong.MaxValue - 1;
            foreach (var vote in Votes) if (vote.Voter == LocalId) mine = vote.Target;

            var grid = UIKit.Layout("vote-grid");
            foreach (var player in NetworkPlayer.All)
            {
                ulong id = player.OwnerClientId;
                if (id == LocalId || !AlivePlayers.Contains(id)) continue;
                var button = UIKit.Button("", () => Vote(id), "vote-option");
                button.Add(UIKit.Swatch(player.Color));
                var label = UIKit.Text(player.DisplayName + (SmearedShown.Contains(id) ? "  (усы!)" : ""), "vote-option__name");
                label.pickingMode = PickingMode.Ignore;
                button.Add(label);
                if (mine == id) button.AddToClassList("vote-option--chosen");
                grid.Add(button);
            }
            var nobody = UIKit.Button("", () => Vote(NoOne), "vote-option", "vote-option--draft");
            var nobodyLabel = UIKit.Text("Никто не ел", "vote-option__name");
            nobodyLabel.pickingMode = PickingMode.Ignore;
            nobody.Add(nobodyLabel);
            if (mine == NoOne) nobody.AddToClassList("vote-option--chosen");
            grid.Add(nobody);
            panel.Add(grid);
            panel.Add(UIKit.Text(mine == ulong.MaxValue - 1 ? "Выбери, на кого думаешь. Можно передумать." : $"Голос принят. Проголосовали {Votes.Count} из {AlivePlayers.Count}.", "muted"));
            return panel;
        }

        private VisualElement BuildVerdict()
        {
            var panel = UIKit.Box("panel");
            ulong accused = Accused.Value;
            if (accused == NoOne && VotedNobody.Value)
                panel.Add(UIKit.Text(RevealedEaters.Count == 0 ? "Решили: никто не ел. И правда!" : "Решили: никто не ел. А зря!", "h1"));
            else if (accused == NoOne) panel.Add(UIKit.Text("Голоса разошлись — никого не наказали", "h1"));
            else panel.Add(UIKit.Text(AccusedAte.Value ? $"{NameOf(accused)} пойман! −3" : $"{NameOf(accused)} не ел — его подставили!", "h1"));

            var eaters = new StringBuilder();
            foreach (ulong id in RevealedEaters) eaters.Append(eaters.Length > 0 ? ", " : "").Append(NameOf(id));
            panel.Add(UIKit.Text(RevealedEaters.Count == 0 ? "На самом деле сметану никто не ел." : $"На самом деле ели: {eaters} (+2 за ложку).", "text"));
            panel.Add(UIKit.Spacer(8));
            foreach (var vote in Votes)
            {
                var row = UIKit.Layout("player-row");
                var voter = NetworkPlayer.FindByClientId(vote.Voter);
                if (voter != null) row.Add(UIKit.Swatch(voter.Color));
                string target = vote.Target == NoOne ? "никто" : NameOf(vote.Target);
                bool right = vote.Target == NoOne ? RevealedEaters.Count == 0 : RevealedEaters.Contains(vote.Target);
                row.Add(UIKit.Text($"{NameOf(vote.Voter)} думал на: {target}", "text", "grow"));
                row.Add(UIKit.Text(right ? "+2" : "мимо", "tag", right ? "tag--ready" : "tag--waiting"));
                panel.Add(row);
            }
            if (Votes.Count == 0) panel.Add(UIKit.Text("Никто не проголосовал.", "muted"));
            return panel;
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        private static string NameOf(ulong id)
        {
            var player = NetworkPlayer.FindByClientId(id);
            return player != null ? player.DisplayName : "кто-то";
        }
    }
}
