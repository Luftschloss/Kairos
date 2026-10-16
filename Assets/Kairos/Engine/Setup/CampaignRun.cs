using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public enum RunStage { Map, Battle, Reward, Rest, Complete, Failed }
    public sealed class CampaignRun
    {
        public DeckList Deck;
        public string Seed;
        public int Life = 20, Encounter;
        public RunStage Stage = RunStage.Map;
        public List<string> Rewards = new List<string>();
        public static readonly string[] EnemyNames = { "催单傀儡", "工位幽灵", "绩效之轮" };
        public static CampaignRun Create(CardDatabase db, int precon, string seed)
        {
            var source = db.Precons[precon];
            var run = new CampaignRun { Seed = seed, Deck = new DeckList { Name = "逆流之塔", Key = "TOWER_STARTER", Keeper = source.Keeper } };
            // Ten low-cost cards from the chosen two-school precon, preserving identity.
            var cards = new List<string>(source.Cards);
            cards.Sort((a, b) => { int cmp = db.Card(a).Time.CompareTo(db.Card(b).Time); return cmp != 0 ? cmp : string.CompareOrdinal(a, b); });
            foreach (string key in cards) if (db.Card(key).Type == "UNIT" && run.Deck.Cards.Count < 6) run.Deck.Cards.Add(key);
            foreach (string key in cards) if (db.Card(key).Type == "SPELL" && !db.Card(key).InterjectOnly && run.Deck.Cards.Count < 10) run.Deck.Cards.Add(key);
            return run;
        }
        public GameState StartBattle(CardDatabase db)
        {
            Rules.Require(Stage == RunStage.Map, "PVE", "当前不是地图阶段");
            var opponent = db.Precons[Encounter == 1 ? 0 : 1];
            var s = GameEngine.Create(new RulesConfig(), db, Deck, opponent, Seed + ":" + Encounter, true);
            s.Players[0].Life = Life;
            s.Players[1].Life = Encounter == 0 ? 12 : Encounter == 1 ? 16 : 20;
            Stage = RunStage.Battle; return s;
        }
        public void FinishBattle(GameState state, CardDatabase db)
        {
            Rules.Require(Stage == RunStage.Battle && state.Outcome != Outcome.Ongoing, "PVE", "战斗尚未结束");
            Life = Math.Max(0, state.Players[0].Life);
            if (state.Outcome != Outcome.P0Win) { Stage = RunStage.Failed; return; }
            if (Encounter == 2) { Stage = RunStage.Complete; return; }
            Stage = RunStage.Reward; Rewards.Clear();
            var pool = new List<string>(); var schools = new List<string>();
            foreach (string key in Deck.Cards) { var d = db.Card(key); if (!schools.Contains(d.School)) schools.Add(d.School); }
            foreach (var d in db.Cards) if (schools.Contains(d.School) && d.Time <= 4 && !d.InterjectOnly) pool.Add(d.Key);
            var rng = new KairosRng(Seed + ":reward:" + Encounter); rng.Shuffle(pool);
            for (int i = 0; i < Math.Min(3, pool.Count); i++) Rewards.Add(pool[i]);
        }
        public void ChooseReward(string key)
        {
            Rules.Require(Stage == RunStage.Reward && (key == null || Rewards.Contains(key)), "PVE", "奖励不合法");
            if (key != null) Deck.Cards.Add(key); Stage = RunStage.Rest;
        }
        public void Rest(int removeIndex = -1)
        {
            Rules.Require(Stage == RunStage.Rest, "PVE", "当前不能休整");
            if (removeIndex >= 0) { Rules.Require(Deck.Cards.Count > 10 && removeIndex < Deck.Cards.Count, "PVE", "牌组至少保留 10 张"); Deck.Cards.RemoveAt(removeIndex); }
            else Life = Math.Min(20, Life + 6);
            Encounter++; Stage = RunStage.Map;
        }
    }
}
