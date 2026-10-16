using System;
using System.Collections.Generic;
using System.IO;
using Kairos.Engine;
using Kairos.Ai;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kairos.Client
{
    public enum DemoMode { Hotseat, Practice, Tower }

    public sealed partial class GameController : MonoBehaviour
    {
        [SerializeField] TextAsset cardsData;
        [SerializeField] RulesConfigAsset rules;
        [SerializeField, Range(0.2f, 3f)] float aiDelay = 1.1f;
        public GameState State { get; private set; }
        public CardDatabase Database { get; private set; }
        public CampaignRun Run { get; private set; }
        public DemoMode Mode { get; private set; }
        public PlayerView View { get; private set; }
        public bool PrivacyCurtain { get; private set; }
        public string Seed = "钟碎之日-001";
        public int FirstDeck, SecondDeck = 1;
        readonly List<GameEvent> history = new List<GameEvent>();
        readonly List<int> selection = new List<int>();
        int viewer, selectedCard, selectedUnit, sand, offset = 1, choiceTop;
        bool ability, placingOmen, help, leaving;
        string prophecy = "UNIT", message;
        Target selectedTarget;
        Command aiCommand;
        float aiAt, replyAt;
        int decisionSerial;
        DeckList[] matchDecks;
        RulesConfig matchConfig;
        string matchSeed;
        int matchStartLife, enemyStartLife;

        void Awake()
        {
            try { Database = CardDatabase.Load(cardsData.text); BuildCanvas(); LoadProgression(); ShowMenu(); if (!TutorialCompleted) ShowTutorialWelcome(null); }
            catch (Exception ex) { InitFailure(ex); }
        }
        public void StartMatch(DemoMode mode)
        {
            MetaTry(() =>
            {
                var own = chosenDeck ?? Database.Precons[FirstDeck]; var other = opponentDeck ?? Database.Precons[SecondDeck];
                progression.StartMatch(mode == DemoMode.Hotseat ? Kairos.Progression.MatchMode.Hotseat : trialMatch ? Kairos.Progression.MatchMode.Trial : Kairos.Progression.MatchMode.Collection, own, other, Seed, rules == null ? new RulesConfig() : rules.Values.Clone());
                EnterSavedMatch(false);
            });
        }
        public void StartTower()
        {
            MetaTry(() => { progression.StartRun(FirstDeck, Seed); lesson = null; tutorialIntro = false; Mode = DemoMode.Tower; SyncRun(); State = null; ShowCampaign(); });
        }
        public void StartTowerBattle()
        {
            MetaTry(() => { progression.StartRunBattle(); EnterSavedMatch(false); });
        }
        void BeginMatch()
        {
            history.Clear(); foreach (var e in State.Events) history.Add(e.Clone());
            decisionSerial++; timelineStart = -1;
            ClearSelection(); message = null; PrivacyCurtain = false; help = false; leaving = false; replyAt = 0; aiCommand = null;
            Refresh();
        }
        void ClearSelection() { selectedAction = null; selectedCard = selectedUnit = sand = 0; ability = placingOmen = false; selectedTarget = default; selection.Clear(); choiceTop = 0; }
        public void Submit(Command command)
        {
            if (State == null) return;
            try
            {
                if (lesson != null)
                {
                    var events = lesson.Apply(command); foreach (var e in events) history.Add(e.Clone());
                    if (lesson.Complete) progression.CompleteTutorial(State.Commands);
                }
                else { State = progression.Advance(command, viewer); SyncRun(); foreach (var e in State.Events) history.Add(e.Clone()); }
                decisionSerial++; ClearSelection(); message = null; replyAt = 0; aiCommand = null; Refresh();
            }
            catch (RulesViolationException ex) { message = ex.Message; RebuildBattle(); }
            catch (Exception ex)
            {
                // A terminal command may be durably saved before reward application fails.
                // Recover that receipt, never submit the command again.
                if (lesson == null) { State = progression.RestoreMatch(); SyncRun(); }
                ShowMetaError(ex.Message, () => { if (lesson != null && lesson.Complete) progression.CompleteTutorial(State.Commands); else progression.RecoverSettlement(); SyncRun(); retrySave = null; ClearSelection(); Refresh(); });
            }
        }
        public void AcceptHandoff()
        {
            viewer = GameEngine.NextActor(State); PrivacyCurtain = false; replyAt = 0; Refresh();
        }
        void Refresh()
        {
            if (State == null) return;
            int actor = GameEngine.NextActor(State);
            if (Mode == DemoMode.Hotseat && actor >= 0 && actor != viewer) { PrivacyCurtain = true; RebuildBattle(); return; }
            View = ViewRedactor.For(State, viewer);
            if (lesson == null && State.Outcome == Outcome.Ongoing && Mode != DemoMode.Hotseat && actor == 1)
            {
                aiCommand = GreedyAi.Choose(ViewRedactor.For(State, 1), Database); aiAt = Time.unscaledTime + aiDelay;
            }
            if (State.Pending != null && State.Pending.Kind == PendingKind.Interject && State.Pending.Player == viewer && replyAt == 0) replyAt = Time.unscaledTime + State.Config.InterjectTimeoutMs / 1000f;
            RebuildBattle();
        }
        void Update()
        {
            if (retrySave != null || State == null || State.Outcome != Outcome.Ongoing || PrivacyCurtain) return;
            if (help || leaving || tutorialIntro) { if (replyAt > 0) replyAt += Time.unscaledDeltaTime; if (aiCommand != null) aiAt += Time.unscaledDeltaTime; return; }
            if (BattleInput && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))) { CancelDraft(); return; }
            if (aiCommand != null && Time.unscaledTime >= aiAt) { var command = aiCommand; aiCommand = null; Submit(command); return; }
            if (replyAt > 0 && State.Pending != null && State.Pending.Kind == PendingKind.Interject && State.Pending.Player == viewer)
            {
                UpdateResponseClock();
                if (Time.unscaledTime >= replyAt) Submit(new Command { Player = viewer, Kind = CommandKind.Pass });
            }
        }
        void ContinueTower()
        {
            CloseResultTo(ShowCampaign);
        }
        void SaveReplay()
        {
            try
            {
                var replay = new ReplayFile { Version = Database.Version, Seed = matchSeed, Config = matchConfig, Decks = matchDecks, Commands = new List<Command>(State.Commands), Campaign = Mode == DemoMode.Tower, InitialLife = new[] { matchStartLife, enemyStartLife } };
                string directory = Path.Combine(Application.persistentDataPath, "Replays"); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "kairos-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"); File.WriteAllText(path, replay.ToJson());
                message = "回放已保存：" + path; RebuildBattle();
            }
            catch (Exception ex) { message = "保存失败：" + ex.Message; RebuildBattle(); }
        }
        Command Draft()
        {
            if (selectedAction.HasValue) return new Command { Player = viewer, Kind = selectedAction.Value };
            if (selectedUnit != 0) return new Command { Player = viewer, Kind = CommandKind.Attack, UnitId = selectedUnit, Target = selectedTarget };
            if (ability) return new Command { Player = viewer, Kind = CommandKind.Ability, Target = selectedTarget };
            if (selectedCard == 0) return null;
            return new Command { Player = viewer, Kind = placingOmen ? CommandKind.Omen : State.Pending?.Kind == PendingKind.Interject ? CommandKind.Interject : CommandKind.Play, CardId = selectedCard, Sand = placingOmen ? 0 : sand, Offset = offset, Target = placingOmen ? default : selectedTarget, Choice = prophecy };
        }
        Command LegalDraft()
        {
            var c = Draft(); if (c == null) return null;
            // Unused choice is harmless; all other validation remains authoritative in Engine.
            return GameEngine.IllegalReason(State, c) == null && LessonAllows(c) ? c : null;
        }
        void SelectTarget(Target target) { if (!BattleInput) return; selectedTarget = target; RebuildBattle(); }
        string TargetName(Target target)
        {
            if (target.Kind == TargetKind.Keeper) return Database.Keeper(View.Players[target.Id].Keeper).Name;
            if (target.Kind == TargetKind.Unit) { foreach (var p in View.Players) foreach (var u in p.Board) if (u.Id == target.Id) return Database.Card(u.Card.Key).Name; }
            if (target.Kind == TargetKind.Object) foreach (var o in View.Timeline) if (o.Id == target.Id) return (o.Card == null ? "未知伏笔" : Database.Card(o.Card.Key).Name) + " @" + o.Pos;
            return "未选择";
        }
        bool CanAct => retrySave == null && State != null && State.Outcome == Outcome.Ongoing && GameEngine.NextActor(State) == viewer && !PrivacyCurtain && !tutorialIntro;
        static string KindName(string type) => type == "UNIT" ? "侍从" : type == "SPELL" ? "术式" : type == "SIGIL" ? "刻印" : "伏笔";
        string EnemyName => lesson != null ? "训练钟偶" : Mode == DemoMode.Tower ? CampaignRun.EnemyNames[Run.Encounter] : "P" + (2 - viewer);
        string AiIntent()
        {
            if (aiCommand == null || State == null) return null;
            var c = aiCommand;
            if (c.Kind == CommandKind.Mulligan) return "敌人正在调度起手";
            if (c.Kind == CommandKind.Choose) return "敌人正在编排未来";
            if (c.Kind == CommandKind.Pass) return "敌人意图：放弃抢拍";
            string action;
            if (c.Kind == CommandKind.Attack) action = "攻击 → " + TargetName(c.Target);
            else if (c.Kind == CommandKind.Omen) action = "埋下伏笔（内容隐藏）";
            else if (c.Kind == CommandKind.Ability) action = Database.Keeper(State.Players[1].Keeper).Ability.Name;
            else if (c.Kind == CommandKind.Focus) action = "蓄势";
            else if (c.Kind == CommandKind.Wait) action = "静候";
            else { var card = GameEngine.FindCard(State.Players[1].Hand, c.CardId); action = card == null ? "行动" : Database.Card(card.Key).Name + (c.Target.Kind == TargetKind.None ? "" : " → " + TargetName(c.Target)); }
            int from = State.Players[1].Marker, to = Math.Min(State.Config.TimelineEnd, from + GameEngine.Cost(State, c));
            return "敌人意图：" + action + "  [" + from + " → " + to + " 刻]";
        }
    }
}
