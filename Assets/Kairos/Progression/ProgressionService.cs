using System;
using System.Collections.Generic;
using System.Linq;
using Kairos.Engine;

namespace Kairos.Progression
{
    // M01-M16: mutations run on a copy; only a successful durable write becomes visible.
    public sealed class ProgressionService
    {
        public const string TutorialGrant = "tutorial-complete-v1";
        readonly CardDatabase db;
        readonly ISaveStore store;
        public SaveEnvelope Data { get; private set; }
        public ProgressionService(CardDatabase db, ISaveStore store, bool legacyTutorial = false)
        {
            this.db = db; this.store = store;
            Data = store.Load();
            if (Data == null)
            {
                var initial = new SaveEnvelope { ContentVersion = db.Version };
                foreach (var deck in db.Precons)
                {
                    initial.Profile.Decks.Add(deck.Clone());
                    if (!initial.Profile.Keepers.Contains(deck.Keeper)) initial.Profile.Keepers.Add(deck.Keeper);
                    foreach (var group in deck.Cards.GroupBy(x => x)) initial.Profile.CardCounts[group.Key] = Math.Max(Owned(initial.Profile, group.Key), group.Count());
                }
                Grant(initial.Profile, "starter-v1", 120); store.Write(initial); Data = initial;
            }
            FileSaveStore.Validate(Data);
            if (legacyTutorial && !Data.Profile.Entitlements.Contains(TutorialGrant)) Transact(s => Grant(s.Profile, TutorialGrant, 40));
            RecoverSettlement();
        }
        void Transact(Action<SaveEnvelope> operation)
        {
            var copy = Data.Clone(); operation(copy); copy.Revision = checked(Data.Revision + 1);
            FileSaveStore.Validate(copy); store.Write(copy); Data = copy;
        }
        public static int Owned(PlayerProfile p, string key) => p.CardCounts.TryGetValue(key, out int n) ? n : 0;
        public static int CraftCost(CardDefinition d) => d.Rarity == "LEGENDARY" ? 240 : d.Rarity == "EPIC" ? 120 : d.Rarity == "RARE" ? 60 : 20;
        public static int Limit(CardDefinition d) => d.Rarity == "LEGENDARY" ? 1 : 2;
        static void Require(bool condition, string text) => Rules.Require(condition, "META", text);
        static bool Grant(PlayerProfile p, string id, int amount)
        { if (!p.Entitlements.Add(id)) return false; p.CraftShards = checked(p.CraftShards + amount); return true; }
        public string DeckReason(DeckList deck, bool owned = true)
        {
            try { db.ValidateDeck(deck, new RulesConfig()); }
            catch (Exception ex) { return ex.Message; }
            if (!owned) return null;
            if (!Data.Profile.Keepers.Contains(deck.Keeper)) return "钟主尚未解锁；可在全卡试用中使用。";
            foreach (var group in deck.Cards.GroupBy(x => x)) if (Owned(Data.Profile, group.Key) < group.Count()) return "缺少收藏：" + db.Card(group.Key).Name;
            return null;
        }
        public Dictionary<string, int> Missing(DeckList deck)
        {
            var result = new Dictionary<string, int>();
            foreach (var group in deck.Cards.GroupBy(x => x))
            { db.Card(group.Key); int count = group.Count() - Owned(Data.Profile, group.Key); if (count > 0) result[group.Key] = count; }
            return result;
        }
        public int Quote(Dictionary<string, int> cards)
        {
            int cost = 0;
            foreach (var pair in cards)
            {
                var d = db.Card(pair.Key); Require(pair.Value > 0 && Owned(Data.Profile, pair.Key) + pair.Value <= Limit(d), "合成数量超出拥有上限");
                cost = checked(cost + CraftCost(d) * pair.Value);
            }
            return cost;
        }
        public void Craft(Dictionary<string, int> cards, string transactionId)
        {
            Require(!string.IsNullOrWhiteSpace(transactionId), "缺少交易编号");
            if (Data.Profile.Transactions.Contains(transactionId)) return;
            int price = Quote(cards); Require(cards.Count > 0 && Data.Profile.CraftShards >= price, "铸片不足或没有缺卡");
            Transact(s => { foreach (var pair in cards) s.Profile.CardCounts[pair.Key] = checked(Owned(s.Profile, pair.Key) + pair.Value); s.Profile.CraftShards -= price; s.Profile.Transactions.Add(transactionId); });
        }
        // Future content grants use a stable entitlement ID, not a card-instance ID.
        public void GrantCard(string key, int count, string entitlement)
        {
            var card = db.Card(key); Require(count > 0 && !string.IsNullOrWhiteSpace(entitlement), "奖励不合法");
            if (Data.Profile.Entitlements.Contains(entitlement)) return;
            Transact(s => { int total = checked(Owned(s.Profile, key) + count), extra = Math.Max(0, total - Limit(card)); s.Profile.CardCounts[key] = Math.Min(total, Limit(card)); s.Profile.CraftShards = checked(s.Profile.CraftShards + extra * (CraftCost(card) / 4)); s.Profile.Entitlements.Add(entitlement); });
        }
        public void SaveDeck(DeckList deck)
        {
            Require(deck != null && !string.IsNullOrWhiteSpace(deck.Key) && !string.IsNullOrWhiteSpace(deck.Name) && deck.Name.Length <= 32 && deck.Cards != null && deck.Cards.Count <= 60, "牌组名称或草稿不合法");
            db.Keeper(deck.Keeper); foreach (string key in deck.Cards) db.Card(key);
            bool legal = DeckReason(deck) == null;
            Transact(s => { int index = s.Profile.Decks.FindIndex(x => x.Key == deck.Key); if (index >= 0) s.Profile.Decks[index] = deck.Clone(); else { Require(s.Profile.Decks.Count < 12, "最多保存 12 副牌组"); s.Profile.Decks.Add(deck.Clone()); } if (legal) Grant(s.Profile, "collector-first-deck-v1", 60); });
        }
        public void DeleteDeck(string key) => Transact(s => { s.Profile.Decks.RemoveAll(x => x.Key == key); });
        public void CompleteTutorial(IEnumerable<Command> commands)
        {
            if (Data.Profile.Entitlements.Contains(TutorialGrant)) return;
            var lesson = TutorialLesson.Create(db); foreach (var c in commands) lesson.Apply(c);
            Require(lesson.Complete, "教学尚未完成"); Transact(s => Grant(s.Profile, TutorialGrant, 40));
        }
        void EnsureFreeSlot()
        {
            Require(Data.ActiveRun == null, "已有保存的旅程，请继续或明确放弃后再开始。");
            Require(Data.ActiveMatch == null, "已有保存的对局，请继续或明确放弃后再开始。");
        }
        public GameState StartMatch(MatchMode mode, DeckList own, DeckList opponent, string seed, RulesConfig config)
        {
            EnsureFreeSlot(); Require(Enum.IsDefined(typeof(MatchMode), mode) && mode != MatchMode.Tower, "请从正确的模式入口开始");
            Require(DeckReason(own, mode == MatchMode.Collection) == null, DeckReason(own, mode == MatchMode.Collection));
            var replay = new ReplayFile { Version = db.Version, Seed = seed, Config = config.Clone(), Decks = new[] { own.Clone(), opponent.Clone() }, Commands = new List<Command>() };
            var state = replay.Play(db);
            Transact(s => { s.ActiveMatch = new SavedMatch { Id = Guid.NewGuid().ToString("N"), Mode = mode, Replay = replay }; });
            return state;
        }
        public void StartRun(int precon, string seed)
        {
            EnsureFreeSlot(); Require(precon >= 0 && precon < db.Precons.Count && Data.Profile.Keepers.Contains(db.Precons[precon].Keeper), "起始方案未开放");
            Transact(s => s.ActiveRun = new SavedRun { Progress = CampaignRun.Create(db, precon, seed) });
        }
        public GameState StartRunBattle()
        {
            Require(Data.ActiveRun != null && Data.ActiveMatch == null, "旅程状态不允许开始战斗");
            GameState state = null;
            Transact(s =>
            {
                var run = s.ActiveRun; state = run.Progress.StartBattle(db);
                s.ActiveMatch = new SavedMatch { Id = Guid.NewGuid().ToString("N"), Mode = MatchMode.Tower, RunId = run.Id, Encounter = run.Progress.Encounter,
                    Replay = new ReplayFile { Version = db.Version, Seed = state.Seed, Config = state.Config.Clone(), Decks = new[] { run.Progress.Deck.Clone(), db.Precons[run.Progress.Encounter == 1 ? 0 : 1].Clone() }, InitialLife = new[] { state.Players[0].Life, state.Players[1].Life }, Campaign = true, Commands = new List<Command>() } };
            }); return state;
        }
        public GameState RestoreMatch() => Data.ActiveMatch?.Replay.Play(db);
        public GameState Advance(Command command, int viewer)
        {
            Require(Data.ActiveMatch != null, "没有进行中的对局");
            Require(CurrentReceipt == null, "本场已经结束，请先处理结算");
            var state = RestoreMatch(); GameEngine.Apply(state, command);
            Transact(s =>
            {
                s.ActiveMatch.Replay.Commands.Add(command.Clone()); s.ActiveMatch.Viewer = viewer;
                if (state.Outcome != Outcome.Ongoing) FixSettlement(s, state);
            });
            RecoverSettlement(); return state;
        }
        void FixSettlement(SaveEnvelope s, GameState state)
        {
            var m = s.ActiveMatch; if (s.Settlements.Any(x => x.MatchId == m.Id)) return;
            bool concede = m.Replay.Commands.Any(c => c.Kind == CommandKind.Concede);
            m.EndReason = concede ? "主动认输 · 本场无收益" : "自然结束";
            bool eligible = !concede && (m.Mode == MatchMode.Collection || m.Mode == MatchMode.Tower);
            var r = new Settlement { CreatedRevision = s.Revision + 1, Id = m.Id + ":result", MatchId = m.Id, RunId = m.RunId, Encounter = m.Encounter, Keeper = m.Replay.Decks[0].Keeper, Outcome = state.Outcome, Reason = eligible ? m.EndReason : m.Mode == MatchMode.Hotseat || m.Mode == MatchMode.Trial ? "试用/热座 · 无成长收益" : m.EndReason };
            if (eligible)
            {
                r.BaseShards = state.Outcome == Outcome.P0Win ? m.Mode == MatchMode.Tower && m.Encounter == 2 ? 40 : 20 : 10; r.Mastery = 10;
                if (!s.Profile.Entitlements.Contains("collector-first-result-v1")) { r.Bonuses.Add("collector-first-result-v1"); r.BonusShards += 60; }
                if (m.Mode == MatchMode.Tower && m.Encounter == 2 && state.Outcome == Outcome.P0Win && !s.Profile.Entitlements.Contains("tower-short-clear-v1")) { r.Bonuses.Add("tower-short-clear-v1"); r.BonusShards += 100; }
            }
            if (m.Mode == MatchMode.Tower && state.Outcome == Outcome.P0Win && m.Encounter < 2)
            {
                var projected = s.Clone().ActiveRun.Progress;
                projected.FinishBattle(state, db);
                r.Candidates = new List<string>(projected.Rewards);
            }
            s.Settlements.Add(r);
        }
        public void RecoverSettlement()
        {
            if (Data.ActiveMatch == null) return;
            var receipt = Data.Settlements.Find(x => x.MatchId == Data.ActiveMatch.Id);
            if (receipt == null)
            {
                var state = RestoreMatch(); if (state.Outcome == Outcome.Ongoing) return;
                Transact(s => FixSettlement(s, state)); receipt = Data.Settlements.Find(x => x.MatchId == Data.ActiveMatch.Id);
            }
            if (receipt.Applied) return;
            Transact(s =>
            {
                var r = s.Settlements.Find(x => x.Id == receipt.Id); var p = s.Profile;
                p.CraftShards = checked(p.CraftShards + r.BaseShards);
                foreach (var bonus in r.Bonuses) Grant(p, bonus, bonus == "tower-short-clear-v1" ? 100 : 60);
                if (r.Mastery > 0) { p.Mastery.TryGetValue(r.Keeper, out int old); p.Mastery[r.Keeper] = checked(old + r.Mastery); }
                r.Applied = true; r.BalanceAfter = p.CraftShards;
                if (s.ActiveMatch.Mode == MatchMode.Tower)
                {
                    Require(s.ActiveRun != null && s.ActiveRun.Id == r.RunId, "旅程归属不一致");
                    var run = s.ActiveRun.Progress; run.FinishBattle(s.ActiveMatch.Replay.Play(db), db); run.Rewards = new List<string>(r.Candidates);
                    if (run.Stage == RunStage.Complete || run.Stage == RunStage.Failed) { run.Deck.Cards.Clear(); run.Rewards.Clear(); r.Candidates.Clear(); }
                }
            });
        }
        public Settlement CurrentReceipt => Data.ActiveMatch == null ? null : Data.Settlements.Find(x => x.MatchId == Data.ActiveMatch.Id);
        public void CloseResult()
        { RecoverSettlement(); Require(CurrentReceipt != null && CurrentReceipt.Applied, "结算未保存"); Transact(s => s.ActiveMatch = null); }
        public void ChooseRunReward(string key, string runId, int encounter)
        {
            var r = Data.Settlements.Find(x => x.RunId == runId && x.Encounter == encounter);
            Require(r != null && r.Applied, "未找到旅程奖励"); if (r.Choice != null) return;
            Transact(s => { Require(s.ActiveRun != null && s.ActiveRun.Id == runId && s.ActiveRun.Progress.Encounter == encounter && s.ActiveMatch == null, "奖励节点不匹配"); s.ActiveRun.Progress.ChooseReward(key); s.Settlements.Find(x => x.Id == r.Id).Choice = key ?? "SKIP"; });
        }
        public void RestRun(int remove, string runId, int encounter)
        { Transact(s => { Require(s.ActiveRun != null && s.ActiveRun.Id == runId && s.ActiveRun.Progress.Encounter == encounter, "休整节点已改变"); s.ActiveRun.Progress.Rest(remove); }); }
        public void AbandonActive()
        { RecoverSettlement(); Transact(s => { s.ActiveMatch = null; s.ActiveRun = null; }); }
        public void FinishRun()
        { Require(Data.ActiveRun != null && Data.ActiveMatch == null && (Data.ActiveRun.Progress.Stage == RunStage.Complete || Data.ActiveRun.Progress.Stage == RunStage.Failed), "旅程尚未结束"); Transact(s => s.ActiveRun = null); }
    }
}
