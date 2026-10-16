using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kairos.Engine;
using Kairos.Ai;
using Kairos.Progression;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class ProgressionTests
    {
        sealed class MemoryStore : ISaveStore
        {
            public string Json; public int Writes, FailAt = -1;
            public SaveEnvelope Load() => Json == null ? null : SaveJson.Read(Json);
            public void Write(SaveEnvelope data) { if (++Writes == FailAt) throw new IOException("test write failure"); Json = SaveJson.Write(data); }
        }
        CardDatabase db;
        [OneTimeSetUp] public void Load()
        {
            string path = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(path, "docs/data/cards.json"))) path = Directory.GetParent(path)?.FullName ?? throw new Exception("Missing data");
            db = CardDatabase.Load(File.ReadAllText(Path.Combine(path, "docs/data/cards.json")));
        }
        ProgressionService New(MemoryStore store, bool tutorial = false) => new ProgressionService(db, store, tutorial);
        void Finish(ProgressionService service)
        {
            var state = service.RestoreMatch();
            for (int i = 0; i < 500 && state.Outcome == Outcome.Ongoing; i++) state = service.Advance(GreedyAi.Choose(ViewRedactor.For(state, GameEngine.NextActor(state)), db), 0);
            Assert.AreNotEqual(Outcome.Ongoing, state.Outcome);
        }
        [Test] public void StarterAndTutorialMigrationAreOnceOnly()
        {
            var store = new MemoryStore(); var s = New(store);
            Assert.AreEqual(32,s.Data.Profile.CardCounts.Count); Assert.AreEqual(59,s.Data.Profile.CardCounts.Values.Sum()); Assert.AreEqual(120,s.Data.Profile.CraftShards);
            foreach(var d in s.Data.Profile.Decks) Assert.IsNull(s.DeckReason(d));
            s=New(store,true); Assert.AreEqual(160,s.Data.Profile.CraftShards); s=New(store,true); Assert.AreEqual(160,s.Data.Profile.CraftShards);
        }
        [Test] public void CraftIsAtomicAndIdempotent()
        {
            var store=new MemoryStore(); var s=New(store); var cards=new Dictionary<string,int>{{"DUSK_ASH_ACOLYTE",2}};
            store.FailAt=store.Writes+1; Assert.Throws<IOException>(()=>s.Craft(cards,"craft1")); Assert.AreEqual(120,s.Data.Profile.CraftShards); Assert.AreEqual(0,ProgressionService.Owned(s.Data.Profile,"DUSK_ASH_ACOLYTE"));
            s.Craft(cards,"craft1"); s.Craft(cards,"craft1"); Assert.AreEqual(80,s.Data.Profile.CraftShards); Assert.AreEqual(2,ProgressionService.Owned(s.Data.Profile,"DUSK_ASH_ACOLYTE"));
            Assert.Throws<RulesViolationException>(()=>s.Craft(cards,"craft2")); Assert.AreEqual(80,s.Data.Profile.CraftShards);
        }
        [Test] public void MissingCardsAndLockedKeeperOnlyAllowedInTrial()
        {
            var s=New(new MemoryStore());var d=db.Precons[0].Clone();d.Keeper="K_NIGHT_SELENE";
            Assert.NotNull(s.DeckReason(d));Assert.IsNull(s.DeckReason(d,false));
            d.Keeper=db.Precons[0].Keeper;d.Cards[0]="NIGHT_FATE_WEAVER";
            Assert.NotNull(s.DeckReason(d));Assert.IsNull(s.DeckReason(d,false));Assert.AreEqual(60,s.Quote(s.Missing(d)));
        }
        [Test] public void SaveDeckMilestoneDoesNotConsumeCollection()
        {
            var s=New(new MemoryStore());var d=db.Precons[0].Clone();d.Key="custom";s.SaveDeck(d);s.SaveDeck(d);s.DeleteDeck(d.Key);s.SaveDeck(d);
            Assert.AreEqual(180,s.Data.Profile.CraftShards);Assert.AreEqual(59,s.Data.Profile.CardCounts.Values.Sum());
            d.Cards.Clear();s.SaveDeck(d);Assert.NotNull(s.DeckReason(d));
        }
        [Test] public void DuplicateGrantConvertsOnlyExcess()
        {var s=New(new MemoryStore());s.GrantCard("DUSK_ASH_ACOLYTE",3,"reward");s.GrantCard("DUSK_ASH_ACOLYTE",3,"reward");Assert.AreEqual(125,s.Data.Profile.CraftShards);Assert.AreEqual(2,ProgressionService.Owned(s.Data.Profile,"DUSK_ASH_ACOLYTE"));}
        [TestCase(MatchMode.Trial)] [TestCase(MatchMode.Hotseat)]
        public void FreeModesNeverReward(MatchMode mode)
        {var s=New(new MemoryStore());s.StartMatch(mode,db.Precons[0],db.Precons[1],"demo-0",new RulesConfig());Finish(s);Assert.AreEqual(120,s.Data.Profile.CraftShards);Assert.AreEqual(0,s.CurrentReceipt.Total);Assert.AreEqual(0,s.CurrentReceipt.Mastery);}
        [Test] public void CollectionSettlementSurvivesReloadAndRepeatedRecovery()
        {
            var store=new MemoryStore();var s=New(store);s.StartMatch(MatchMode.Collection,db.Precons[0],db.Precons[1],"demo-0",new RulesConfig());Finish(s);
            int balance=s.Data.Profile.CraftShards;Assert.AreEqual(60,s.CurrentReceipt.BonusShards);Assert.AreEqual(10,s.CurrentReceipt.Mastery);
            s=New(store);s.RecoverSettlement();Assert.AreEqual(balance,s.Data.Profile.CraftShards);s.CloseResult();Assert.IsNull(s.Data.ActiveMatch);
        }
        [Test] public void InterruptedApplicationRetriesFixedReceipt()
        {
            var store=new MemoryStore();var s=New(store);s.StartMatch(MatchMode.Collection,db.Precons[0],db.Precons[1],"demo-0",new RulesConfig());
            while(true){var state=s.RestoreMatch();var c=GreedyAi.Choose(ViewRedactor.For(state,GameEngine.NextActor(state)),db);GameEngine.Apply(state,c);if(state.Outcome!=Outcome.Ongoing){store.FailAt=store.Writes+2;Assert.Throws<IOException>(()=>s.Advance(c,0));break;}s.Advance(c,0);}
            Assert.IsFalse(s.CurrentReceipt.Applied);Assert.AreEqual(120,s.Data.Profile.CraftShards);string id=s.CurrentReceipt.Id;
            s=New(store);Assert.IsTrue(s.CurrentReceipt.Applied);Assert.AreEqual(id,s.CurrentReceipt.Id);int money=s.Data.Profile.CraftShards;s.RecoverSettlement();Assert.AreEqual(money,s.Data.Profile.CraftShards);
        }
        [Test] public void ConcedeNeverCountsAsNaturalLoss()
        {var s=New(new MemoryStore());s.StartMatch(MatchMode.Collection,db.Precons[0],db.Precons[1],"x",new RulesConfig());s.Advance(new Command{Player=0,Kind=CommandKind.Concede},0);Assert.AreEqual(120,s.Data.Profile.CraftShards);Assert.AreEqual(0,s.CurrentReceipt.Mastery);}
        [Test] public void EverySavedDecisionRebuildsIdentically()
        {
            var store=new MemoryStore();var s=New(store);var state=s.StartMatch(MatchMode.Collection,db.Precons[0],db.Precons[1],"resume",new RulesConfig());
            for(int i=0;i<20&&state.Outcome==Outcome.Ongoing;i++){state=s.Advance(GreedyAi.Choose(ViewRedactor.For(state,GameEngine.NextActor(state)),db),0);s=New(store);Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(state),Newtonsoft.Json.JsonConvert.SerializeObject(s.RestoreMatch()));}
        }
        [Test] public void RunConcedeClearsTemporaryCardsNotCollection()
        {var store=new MemoryStore();var s=New(store);s.StartRun(0,"tower");s.StartRunBattle();s.Advance(new Command{Player=0,Kind=CommandKind.Concede},0);Assert.AreEqual(RunStage.Failed,s.Data.ActiveRun.Progress.Stage);Assert.IsEmpty(s.Data.ActiveRun.Progress.Deck.Cards);Assert.AreEqual(59,s.Data.Profile.CardCounts.Values.Sum());s.CloseResult();s.FinishRun();Assert.IsNull(s.Data.ActiveRun);}
        [Test] public void RunRewardAndRestRemainAfterReload()
        {
            var store=new MemoryStore();var s=New(store);s.StartRun(0,"tower-test");var state=s.StartRunBattle();
            // Legal fixed policy: player fights with AI; opponent only waits/passes.
            for(int i=0;i<500&&state.Outcome==Outcome.Ongoing;i++){int actor=GameEngine.NextActor(state);var view=ViewRedactor.For(state,actor);var c=actor==0?GreedyAi.Choose(view,db):view.Legal.FirstOrDefault(x=>x.Kind==CommandKind.Wait||x.Kind==CommandKind.Pass)??view.Legal[0];state=s.Advance(c,0);}
            Assert.AreEqual(Outcome.P0Win,state.Outcome);s.CloseResult();string id=s.Data.ActiveRun.Id;var rewards=s.Data.ActiveRun.Progress.Rewards.ToArray();s=New(store);CollectionAssert.AreEqual(rewards,s.Data.ActiveRun.Progress.Rewards);
            s.ChooseRunReward(rewards[0],id,0);s.ChooseRunReward(rewards[0],id,0);Assert.AreEqual(11,s.Data.ActiveRun.Progress.Deck.Cards.Count);s.RestRun(-1,id,0);s=New(store);Assert.AreEqual(1,s.Data.ActiveRun.Progress.Encounter);Assert.Throws<RulesViolationException>(()=>s.RestRun(-1,id,0));
        }
        [Test] public void MissingSchemaNeverCreatesAnEmptyAccount()
        {
            var store = new MemoryStore { Json = "{}" };
            Assert.Throws<InvalidDataException>(() => New(store)); Assert.AreEqual("{}", store.Json); Assert.AreEqual(0, store.Writes);
        }
        [Test] public void CorruptPrimaryUsesBackupWithoutDestroyingIt()
        {
            string dir=Path.Combine(Directory.GetCurrentDirectory(),"Library/Locus/tmp/meta-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"profile.json");
            try{var fs=new FileSaveStore(path);var s=new ProgressionService(db,fs);s.SaveDeck(db.Precons[0]);File.WriteAllText(path,"broken");fs=new FileSaveStore(path);s=new ProgressionService(db,fs);Assert.NotNull(fs.Notice);Assert.AreEqual(120,s.Data.Profile.CraftShards);s.SaveDeck(db.Precons[0]);Assert.DoesNotThrow(()=>SaveJson.Read(File.ReadAllText(path+".bak")));File.WriteAllText(path,"broken");File.WriteAllText(path+".bak","broken");Assert.Throws<IOException>(()=>new ProgressionService(db,new FileSaveStore(path)));Assert.AreEqual("broken",File.ReadAllText(path));}
            finally{Directory.Delete(dir,true);}
        }
    }
}
