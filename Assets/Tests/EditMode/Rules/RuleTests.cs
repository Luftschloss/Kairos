using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kairos.Engine;
using Kairos.Ai;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class RuleTests
    {
        CardDatabase db;
        [OneTimeSetUp] public void Load()
        {
            string path = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(path, "docs/data/cards.json"))) path = Directory.GetParent(path)?.FullName ?? throw new Exception("Cannot locate docs/data/cards.json");
            db = CardDatabase.Load(File.ReadAllText(Path.Combine(path, "docs/data/cards.json")));
        }
        GameState Empty(int marker = 0, int opponent = 0)
        {
            var s = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "test");
            foreach (var p in s.Players) { p.Hand.Clear(); p.Board.Clear(); p.Past.Clear(); p.MulliganDone = true; p.LastMoveSeq = 0; }
            s.Players[0].Marker = marker; s.Players[1].Marker = opponent; s.Players[0].LastMoveSeq = 1; s.Events.Clear(); return s;
        }
        CardInstance Hand(GameState s, int p, string key) { var c = new CardInstance { Id = s.NextId++, Owner = p, Key = key }; s.Players[p].Hand.Add(c); return c; }
        Unit Unit(GameState s, int p, string key, bool exhausted = false)
        {
            var d = db.Card(key); var u = new Unit { Id = s.NextId++, Card = new CardInstance { Id = s.NextId++, Owner = p, Key = key }, Atk = d.Atk, Hp = d.Hp, MaxHp = d.Hp, Cadence = d.Cadence, Keywords = d.Keywords, Exhausted = exhausted }; s.Players[p].Board.Add(u); return u;
        }
        Command Play(GameState s, string key, Target target = default, int p = 0) => new Command { Player = p, Kind = CommandKind.Play, CardId = Hand(s, p, key).Id, Target = target };
        static Command Wait(int p = 0) => new Command { Player = p, Kind = CommandKind.Wait };
        static void FinishPending(GameState s) { while (s.Pending != null) { var list = new List<Command>(); GameEngine.LegalCommands(s, s.Pending.Player, list); GameEngine.Apply(s, list[0]); } }
        static string Snapshot(GameState s) => JsonConvert.SerializeObject(new { s.Players, s.Timeline, s.Pending, s.InFlight, s.Frames, s.NextId, s.Rng, s.Outcome, s.LastDeclaredActor });

        [Test] public void Data_46Cards10KeepersTwoLegalPrecons() { Assert.AreEqual(46, db.Cards.Count); Assert.AreEqual(10, db.Keepers.Count); foreach (var d in db.Precons) { Assert.AreEqual(30, d.Cards.Count); db.ValidateDeck(d, new RulesConfig()); } }
        [Test] public void T01_LaggingPlayerActs() { Assert.AreEqual(0, GameEngine.NextActor(Empty(3, 5))); }
        [Test] public void T02_TieLaterArrival() { var s = Empty(4, 4); s.Players[0].LastMoveSeq = 7; s.Players[1].LastMoveSeq = 9; Assert.AreEqual(1, GameEngine.NextActor(s)); }
        [Test] public void T03_OpeningCompensationAndMulliganOnce()
        {
            var s = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "opening");
            int first = s.Players[0].LastMoveSeq == 1 ? 0 : 1; Assert.AreEqual(5, s.Players[first].Hand.Count); Assert.AreEqual(6, s.Players[1 - first].Hand.Count);
            GameEngine.Apply(s, new Command { Kind = CommandKind.Mulligan, Player = 1, Selection = new[] { s.Players[1].Hand[0].Id } });
            Assert.NotNull(GameEngine.IllegalReason(s, new Command { Kind = CommandKind.Mulligan, Player = 1 }));
            GameEngine.Apply(s, new Command { Kind = CommandKind.Mulligan, Player = 0 }); Assert.AreEqual(first, GameEngine.NextActor(s));
        }
        [Test] public void T04_OverpaySandConsumedAndCostFloor()
        {
            var s = Empty(); s.Players[0].Sand = 3; var c = Play(s, "N_BELL_GUARD"); c.Sand = 3;
            Assert.AreEqual(1, GameEngine.Cost(s, c)); GameEngine.Apply(s, c); Assert.AreEqual(0, s.Players[0].Sand); Assert.AreEqual(1, s.Players[0].Marker);
        }
        [Test] public void T05_IllegalCommandIsAtomic()
        { var s = Empty(); var c = Play(s, "N_WANDERING_TIMEKEEPER"); string before = Snapshot(s); Assert.Throws<RulesViolationException>(() => GameEngine.Apply(s, c)); Assert.AreEqual(before, Snapshot(s)); }
        [Test] public void T06_FinalCostUsesSandForHorizon() { var s = Empty(); s.Players[0].Sand = 2; var c = Play(s, "N_BRASS_GOLEM"); c.Sand = 2; Assert.IsNull(GameEngine.IllegalReason(s, c)); }
        [Test] public void T07_WaitCrossesTwoBells()
        { var s = Empty(3, 8); var events = GameEngine.Apply(s, Wait()); Assert.AreEqual(9, s.Players[0].Marker); Assert.AreEqual(2, s.Players[0].ChimesRung); Assert.AreEqual(2, events.Count(e => e.Kind == "Chime")); }
        [Test] public void T08_OldBellNeverRingsAgain()
        { var s = Empty(7, 8); s.Players[0].ChimesRung = 2; GameEngine.Apply(s, Wait()); Assert.AreEqual(0, s.Events.Count(e => e.Kind == "Chime")); }
        [Test] public void T09_KairosUsesActualHourglassOneCost()
        { var s = Empty(3, 5); GameEngine.Apply(s, Play(s, "N_HOURGLASS")); Assert.AreEqual(3, s.Players[0].Hand.Count); Assert.IsTrue(s.Events.Any(e => e.Text.Contains("契机"))); }
        [Test] public void T10_64IsNotKairos()
        { var s = Empty(63, 64); s.Players[1].Finished = true; GameEngine.Apply(s, Play(s, "N_HOURGLASS")); Assert.IsFalse(s.Events.Any(e => e.Text.Contains("契机"))); }
        [Test] public void T11_InterjectBreaksChain()
        {
            var s = Empty(0, 0); var response = Hand(s, 1, "DAWN_FIRST_LIGHT_STRIKE"); GameEngine.Apply(s, Play(s, "DAWN_SCOUT"));
            GameEngine.Apply(s, new Command { Kind = CommandKind.Interject, Player = 1, CardId = response.Id, Target = new Target(TargetKind.Keeper, 0) });
            Assert.AreEqual(1, s.LastDeclaredActor); Assert.AreEqual(2, s.Players[1].Marker);
            GameEngine.Apply(s, Play(s, "DAWN_COURIER")); Assert.IsFalse(s.Events.Any(e => e.Kind == "Declared" && e.Text.Contains("连拍")));
        }
        [Test] public void T12_T13_SingleResponseKillsAttackerStillPays()
        {
            var s = Empty(); var u = Unit(s, 0, "DAWN_SCOUT"); var response = Hand(s, 1, "DAWN_FIRST_LIGHT_STRIKE");
            GameEngine.Apply(s, new Command { Kind = CommandKind.Attack, Player = 0, UnitId = u.Id, Target = new Target(TargetKind.Keeper, 1) });
            Assert.AreEqual(PendingKind.Interject, s.Pending.Kind);
            GameEngine.Apply(s, new Command { Kind = CommandKind.Interject, Player = 1, CardId = response.Id, Target = new Target(TargetKind.Unit, u.Id) });
            Assert.IsNull(s.Pending); Assert.AreEqual(1, s.Players[0].Marker); Assert.AreEqual(2, s.Players[1].Marker); Assert.AreEqual(20, s.Players[1].Life); Assert.IsNull(GameEngine.FindUnit(s, u.Id));
        }
        [Test] public void T14_OmenPlacementUsesPostCost()
        {
            var s = Empty(3, 7); var card = Hand(s, 0, "NIGHT_VOID_GAZE"); var c = new Command { Kind = CommandKind.Omen, Player = 0, CardId = card.Id, Offset = 1 };
            Assert.NotNull(GameEngine.IllegalReason(s, c)); c.Offset = 3; Assert.IsNull(GameEngine.IllegalReason(s, c)); GameEngine.Apply(s, c); Assert.AreEqual(8, s.Timeline[0].Pos);
        }
        [Test] public void T15_BluffStillTriggersWeaver()
        {
            var s = Empty(0, 0); Unit(s, 0, "NIGHT_FATE_WEAVER"); var card = Hand(s, 0, "DAWN_SCOUT");
            GameEngine.Apply(s, new Command { Kind = CommandKind.Omen, Player = 0, CardId = card.Id, Offset = 1 }); GameEngine.Apply(s, Wait(1));
            Assert.AreEqual(1, s.Players[0].Hand.Count); Assert.AreEqual(1, s.Players[0].Past.Count); Assert.AreEqual(1, s.Players[0].Board.Count);
        }
        [Test] public void T16_PushCrossesOmenAndTargetsMover()
        {
            var s = Empty(3, 7); var c = Hand(s, 0, "NIGHT_VOID_GAZE"); GameEngine.Apply(s, new Command { Kind = CommandKind.Omen, Player = 0, CardId = c.Id, Offset = 4 });
            GameEngine.Apply(s, Play(s, "ETERNAL_SLOW_CURRENT")); Assert.AreEqual(9, s.Players[1].Marker); Assert.AreEqual(2, s.Players[1].Statuses[0].Delta);
        }
        [Test] public void T17_TickOrderOmenChannelChime()
        {
            var s = Empty(3, 8);
            var star = Play(s, "NIGHT_STARFALL", new Target(TargetKind.Keeper, 1)); GameEngine.Apply(s, star);
            var card = Hand(s, 0, "NIGHT_VOID_GAZE"); s.Players[1].Marker = 4;
            GameEngine.Apply(s, new Command { Kind = CommandKind.Omen, Player = 0, CardId = card.Id, Offset = 2 });
            s.Players[1].Marker = 8; GameEngine.Apply(s, Wait());
            var kinds = s.Events.Select(e => e.Kind).ToList(); Assert.Less(kinds.IndexOf("OmenReveal"), kinds.IndexOf("Channel")); Assert.Less(kinds.IndexOf("Channel"), kinds.IndexOf("Chime"));
        }
        [Test] public void T18_ChannelNeverRetargets()
        {
            var s = Empty(0, 4); var enemy = Unit(s, 1, "DAWN_SCOUT"); GameEngine.Apply(s, Play(s, "NIGHT_STARFALL", new Target(TargetKind.Unit, enemy.Id)));
            s.Players[1].Board.Clear(); GameEngine.Apply(s, Wait()); Assert.AreEqual(20, s.Players[1].Life); Assert.AreEqual(0, s.Timeline.Count);
        }
        [Test] public void T19_GuardAndInterceptorRedirect()
        {
            var s = Empty(); s.Players[1].ChimesRung = 1; var attacker = Unit(s, 0, "N_BRASS_GOLEM"); var guard = Unit(s, 1, "N_BELL_GUARD"); var card = Hand(s, 1, "DAWN_INTERCEPTOR");
            var attack = new Command { Kind = CommandKind.Attack, Player = 0, UnitId = attacker.Id, Target = new Target(TargetKind.Keeper, 1) }; Assert.NotNull(GameEngine.IllegalReason(s, attack));
            attack.Target = new Target(TargetKind.Unit, guard.Id); GameEngine.Apply(s, attack); GameEngine.Apply(s, new Command { Kind = CommandKind.Interject, Player = 1, CardId = card.Id }); Assert.AreEqual(3, guard.Hp); Assert.AreEqual(3, attacker.Hp);
        }
        [Test] public void T20_StasisTakesTwoChimes()
        {
            var s = Empty(2, 2); var unit = Unit(s, 0, "DAWN_SCOUT"); unit.Stasis = true; unit.Exhausted = true; s.LastDeclaredActor = 0;
            GameEngine.Apply(s, Play(s, "DAWN_GALE_CHORUS", new Target(TargetKind.Unit, unit.Id))); Assert.IsFalse(unit.Stasis); Assert.IsTrue(unit.Exhausted);
            s.Players[1].Marker = 8; GameEngine.Apply(s, Wait()); Assert.IsFalse(unit.Exhausted);
        }
        [Test] public void T21_RefluxAtCheckpointOnlyOnce()
        {
            var s = Empty(19, 20); s.Players[1].Life = 12; s.Players[0].ChimesRung = s.Players[1].ChimesRung = 5;
            GameEngine.Apply(s, Play(s, "N_PENDULUM_BLADE", new Target(TargetKind.Keeper, 1))); Assert.AreEqual(9, s.Players[1].Life); Assert.AreEqual(18, s.Players[1].Marker); Assert.IsTrue(s.Players[1].RefluxUsed);
        }
        [Test] public void T22_DuskDamage()
        { var s = Empty(47, 52); s.Players[0].ChimesRung = 11; GameEngine.Apply(s, Wait()); Assert.AreEqual(17, s.Players[0].Life); }
        [Test] public void T23_TimeEndUsesAttackTiebreak()
        { var s = Empty(63, 64); s.Players[1].Finished = true; Unit(s, 0, "N_BRASS_GOLEM"); Unit(s, 1, "DAWN_SCOUT"); GameEngine.Apply(s, Wait()); Assert.AreEqual(Outcome.P0Win, s.Outcome); }
        [Test] public void T24_FullHandBurns()
        { var s = Empty(3, 4); for (int i = 0; i < 8; i++) Hand(s, 0, "N_HOURGLASS"); GameEngine.Apply(s, Wait()); Assert.AreEqual(8, s.Players[0].Hand.Count); Assert.AreEqual(1, s.Players[0].Past.Count); }
        [Test] public void T25_EmptyDeckDamage()
        { var s = Empty(); s.Players[0].Deck.Clear(); GameEngine.Apply(s, new Command { Kind = CommandKind.Focus, Player = 0 }); Assert.AreEqual(18, s.Players[0].Life); }
        [Test] public void T26_PushCapPerBar()
        { var s = Empty(0, 4); s.Players[1].PushReceived = 3; s.Players[1].ChimesRung = 1; GameEngine.Apply(s, Play(s, "ETERNAL_SLOW_CURRENT")); Assert.AreEqual(5, s.Players[1].Marker); }
        [Test] public void T27_ProphecyConsumesOnDeclareDrawsAtCheckpoint()
        {
            var s = Empty(); var unit = Unit(s, 1, "DAWN_SCOUT"); var c = Play(s, "NIGHT_PROPHECY"); c.Choice = "ATTACK"; GameEngine.Apply(s, c);
            GameEngine.Apply(s, new Command { Kind = CommandKind.Attack, Player = 1, UnitId = unit.Id, Target = new Target(TargetKind.Keeper, 0) }); Assert.AreEqual(2, s.Players[1].Marker); Assert.AreEqual(2, s.Players[0].Hand.Count); Assert.IsNull(s.Players[1].Prophecy);
        }
        [Test] public void T28_SimultaneousLethalDraw()
        { var s = Empty(); s.Players[0].Life = s.Players[1].Life = 0; GameEngine.Apply(s, new Command { Player = 0, Kind = CommandKind.Focus }); Assert.AreEqual(Outcome.Draw, s.Outcome); }
        [Test] public void T29_DiscountConsumedEvenAtFloor()
        { var s = Empty(); s.Players[0].Sigils.Add(new CardInstance { Key = "DAWN_LAW_OF_INITIATIVE", Owner = 0, Id = s.NextId++ }); s.LastDeclaredActor = 0; GameEngine.Apply(s, Play(s, "DAWN_SCOUT")); Assert.AreEqual(1, s.Players[0].Marker); Assert.IsTrue(s.Players[0].ChainDiscountUsed); }
        [Test] public void T30_JudgeAddsChannelCost()
        { var s = Empty(); Unit(s, 1, "NOON_SUNDIAL_JUDGE"); var c = Play(s, "NIGHT_STARFALL", new Target(TargetKind.Keeper, 1)); Assert.AreEqual(2, GameEngine.Cost(s, c)); }
        [Test] public void T31_T32_NormalInterjectCardNoTaxButOnlyRestricted()
        { var s = Empty(); Assert.AreEqual(1, GameEngine.Cost(s, Play(s, "DAWN_FIRST_LIGHT_STRIKE", new Target(TargetKind.Keeper, 1)))); Assert.NotNull(GameEngine.IllegalReason(s, Play(s, "DAWN_INTERCEPTOR"))); }
        [Test] public void T33_FinishedOpponentWaitClampsAt64()
        { var s = Empty(50, 64); s.Players[1].Finished = true; s.Players[0].ChimesRung = 12; GameEngine.Apply(s, Wait()); Assert.AreEqual(64, s.Players[0].Marker); Assert.AreEqual(11, s.Players[0].Life); Assert.IsTrue(s.Players[0].Finished); }
        [Test] public void T34_UnresolvedChannelDiscardedAtFinish()
        { var s = Empty(63, 64); s.Players[1].Finished = true; GameEngine.Apply(s, Play(s, "NIGHT_STARFALL", new Target(TargetKind.Keeper, 1))); Assert.AreEqual(0, s.Timeline.Count); Assert.AreEqual(1, s.Players[0].Past.Count); }
        [Test] public void T36_PassiveChoiceRejected()
        {
            var root = new Newtonsoft.Json.Linq.JObject { ["version"] = "test", ["cards"] = new Newtonsoft.Json.Linq.JArray(Newtonsoft.Json.Linq.JObject.Parse("{ 'key':'bad', 'type':'UNIT', 'hp':1, 'abilities':[{'trigger':'ON_DEATH','effects':[{'op':'SCRY','count':1}]}]}")), ["keepers"] = new Newtonsoft.Json.Linq.JArray(), ["precons"] = new Newtonsoft.Json.Linq.JArray() };
            var e = Assert.Throws<RulesViolationException>(() => CardDatabase.Load(root.ToString())); Assert.AreEqual("R-9-4", e.Rule);
        }
        [Test] public void T37_CloneDuringChoiceIndependentAndDeterministic()
        { var s = Empty(); GameEngine.Apply(s, Play(s, "NIGHT_STARGAZER_APPRENTICE")); Assert.AreEqual(PendingKind.Scry, s.Pending.Kind); var clone = s.Clone(); var options = new List<Command>(); GameEngine.LegalCommands(s, 0, options); GameEngine.Apply(s, options[0]); Assert.NotNull(clone.Pending); GameEngine.Apply(clone, options[0]); Assert.AreEqual(Snapshot(s), Snapshot(clone)); }
        [Test] public void T38_RecallNoDeployAndExhaustedEvenSwift()
        { var s = Empty(4, 5); s.Players[0].ChimesRung = 1; s.Players[0].Past.Add(new CardInstance { Id = s.NextId++, Owner = 0, Key = "NIGHT_STARGAZER_APPRENTICE" }); GameEngine.Apply(s, Play(s, "DUSK_ECHO_OF_THE_DEAD")); Assert.AreEqual(PendingKind.Summon, s.Pending.Kind); FinishPending(s); Assert.IsNull(s.Pending); Assert.AreEqual(2, s.Players[0].Board.Count); Assert.IsTrue(s.Players[0].Board[1].Exhausted); }
        [Test] public void T39_ReplayExactState()
        {
            var s = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "replay");
            for (int i = 0; i < 100 && s.Outcome == Outcome.Ongoing; i++) GameEngine.Apply(s, GreedyAi.Choose(ViewRedactor.For(s, GameEngine.NextActor(s)), db));
            var replay = new ReplayFile { Version = db.Version, Config = s.Config, Seed = s.Seed, Decks = new[] { db.Precons[0], db.Precons[1] }, Commands = s.Commands };
            Assert.AreEqual(Snapshot(s), Snapshot(ReplayFile.FromJson(replay.ToJson()).Play(db)));
        }
        [Test] public void HiddenInformationProjection()
        {
            var s = Empty(); Hand(s, 1, "NIGHT_MIRROR_MOMENT"); var card = Hand(s, 0, "NIGHT_VOID_GAZE"); GameEngine.Apply(s, new Command { Player = 0, Kind = CommandKind.Omen, CardId = card.Id, Offset = 1 });
            var view = ViewRedactor.For(s, 1); Assert.AreEqual(0, view.Players[0].Hand.Count); Assert.AreEqual(0, view.Players[0].Deck.Count); Assert.AreEqual(0, view.Players[1].Deck.Count); Assert.IsNull(view.Timeline[0].Card); Assert.IsNull(view.Timeline[0].Context);
        }
        [Test] public void CampaignStateMachineAndReplayInitialLife()
        {
            var run = CampaignRun.Create(db, 0, "tower"); Assert.AreEqual(10, run.Deck.Cards.Count);
            for (int encounter = 0; encounter < 3; encounter++)
            {
                var s = run.StartBattle(db); Assert.AreEqual(run.Life, s.Players[0].Life); s.Outcome = Outcome.P0Win; s.Players[0].Life = 13; run.FinishBattle(s, db);
                if (encounter < 2) { Assert.AreEqual(RunStage.Reward, run.Stage); Assert.AreEqual(3, run.Rewards.Count); run.ChooseReward(run.Rewards[0]); run.Rest(); Assert.AreEqual(19, run.Life); }
            }
            Assert.AreEqual(RunStage.Complete, run.Stage);
        }
        [Test] public void TargetFilterRevalidatedAfterInterject()
        {
            var s = Empty(12, 13); s.Players[0].ChimesRung = s.Players[1].ChimesRung = 3;
            var victim = Unit(s, 1, "DUSK_REAPER"); var response = Hand(s, 1, "DUSK_WITHERING_TOUCH");
            GameEngine.Apply(s, Play(s, "NOON_NOON_JUDGMENT", new Target(TargetKind.Unit, victim.Id)));
            GameEngine.Apply(s, new Command { Player = 1, Kind = CommandKind.Interject, CardId = response.Id, Target = new Target(TargetKind.Unit, victim.Id) });
            Assert.NotNull(GameEngine.FindUnit(s, victim.Id)); Assert.AreEqual(2, victim.Atk); Assert.IsTrue(s.Events.Any(e => e.Kind == "Fizzle"));
        }
        [Test] public void CounterReturnsOriginalCardToDeckBottomStillPays()
        {
            var s = Empty(12, 13); s.Players[0].ChimesRung = s.Players[1].ChimesRung = 3;
            var response = Hand(s, 1, "NIGHT_TEMPORAL_REWIND"); var spell = Play(s, "NIGHT_STARFALL", new Target(TargetKind.Keeper, 1));
            GameEngine.Apply(s, spell); GameEngine.Apply(s, new Command { Player = 1, Kind = CommandKind.Interject, CardId = response.Id });
            Assert.AreEqual(13, s.Players[0].Marker); Assert.AreEqual(spell.CardId, s.Players[0].Deck.Last().Id); Assert.AreEqual(0, s.Timeline.Count);
        }
        [Test] public void EveryCardCanResolveThroughDsl()
        {
            foreach (var definition in db.Cards)
            {
                var s = Empty(20, 21); s.Players[0].ChimesRung = s.Players[1].ChimesRung = 5;
                Unit(s, 0, "N_BELL_GUARD", true); Unit(s, 1, "N_BRASS_GOLEM");
                s.Players[0].Past.Add(new CardInstance { Id = s.NextId++, Owner = 0, Key = "DAWN_SCOUT" });
                var card = Hand(s, 0, definition.Key);
                if (definition.InterjectOnly)
                {
                    var declaration = definition.InterjectCondition == "DECLARED_ATTACK" ? new Command { Kind = CommandKind.Attack, Player = 1, UnitId = s.Players[1].Board[0].Id, Target = new Target(TargetKind.Unit, s.Players[0].Board[0].Id) } : Play(s, "N_HOURGLASS", default, 1);
                    s.Players[1].Marker = 19; GameEngine.Apply(s, declaration);
                }
                if (definition.Targets.Any(t => t.Selector == "TIMELINE_OBJECT"))
                {
                    s.Timeline.Add(new TimelineObject { Id = s.NextId++, Owner = 1, Pos = 25, Kind = ObjectKind.Channel, Card = new CardInstance { Id = s.NextId++, Owner = 1, Key = "NIGHT_STARFALL" }, Context = new EffectContext { Player = 1, Target = new Target(TargetKind.Keeper, 0) } });
                }
                var legal = new List<Command>(); GameEngine.LegalCommands(s, 0, legal);
                var command = legal.FirstOrDefault(c => c.CardId == card.Id && (definition.Type == "OMEN" ? c.Kind == CommandKind.Omen : c.Kind == (definition.InterjectOnly ? CommandKind.Interject : CommandKind.Play)));
                Assert.NotNull(command, definition.Key); GameEngine.Apply(s, command); FinishPending(s);
                for (int n = 0; n < 12 && s.Timeline.Count > 0 && s.Outcome == Outcome.Ongoing; n++) { GameEngine.Apply(s, Wait(GameEngine.NextActor(s))); FinishPending(s); }
            }
        }
        [Test] public void EveryKeeperAbilityResolves()
        {
            foreach (var keeper in db.Keepers)
            {
                var s = Empty(4, 6); s.Players[0].Keeper = keeper.Key; s.Players[0].ChimesRung = 1;
                var unit = Unit(s, 0, "DAWN_SCOUT", true); unit.EnteredAtChime = 1;
                Unit(s, 1, "N_BRASS_GOLEM");
                s.Players[1].LastPlayedCardKey = "N_HOURGLASS";
                s.Players[0].Past.Add(new CardInstance { Id = s.NextId++, Owner = 0, Key = "DAWN_SCOUT" });
                s.Timeline.Add(new TimelineObject { Id = s.NextId++, Owner = 1, Pos = 15, Kind = ObjectKind.Channel, Card = new CardInstance { Id = s.NextId++, Owner = 1, Key = "NIGHT_STARFALL" }, Context = new EffectContext { Player = 1 } });
                var legal = new List<Command>(); GameEngine.LegalCommands(s, 0, legal); var command = legal.FirstOrDefault(c => c.Kind == CommandKind.Ability);
                Assert.NotNull(command, keeper.Key); GameEngine.Apply(s, command); FinishPending(s); Assert.Greater(s.Players[0].AbilityReadyAt, 1);
            }
        }
        [Test] public void CloneOfResponseWindowDoesNotShareCommands()
        {
            var s = Empty(); Hand(s, 1, "DAWN_FIRST_LIGHT_STRIKE"); GameEngine.Apply(s, Play(s, "DAWN_SCOUT")); var copy = s.Clone();
            GameEngine.Apply(copy, new Command { Kind = CommandKind.Pass, Player = 1 }); Assert.NotNull(s.Pending); Assert.AreEqual(0, s.Players[0].Board.Count); Assert.AreEqual(1, copy.Players[0].Board.Count);
        }
        [Test] public void CampaignFailureAndRemoveInsteadOfHeal()
        {
            var run = CampaignRun.Create(db, 0, "lose"); var s = run.StartBattle(db); s.Outcome = Outcome.P1Win; run.FinishBattle(s, db); Assert.AreEqual(RunStage.Failed, run.Stage);
            run = CampaignRun.Create(db, 0, "remove"); s = run.StartBattle(db); s.Outcome = Outcome.P0Win; s.Players[0].Life = 11; run.FinishBattle(s, db); run.ChooseReward(run.Rewards[0]); run.Rest(0); Assert.AreEqual(10, run.Deck.Cards.Count); Assert.AreEqual(11, run.Life);
        }
        [Test] public void ReplayKeepsCampaignStartingLife()
        {
            var replay = new ReplayFile { Version = db.Version, Config = new RulesConfig(), Seed = "life", Decks = new[] { db.Precons[0], db.Precons[1] }, Commands = new List<Command>(), Campaign = true, InitialLife = new[] { 9, 12 } };
            var s = ReplayFile.FromJson(replay.ToJson()).Play(db); Assert.AreEqual(9, s.Players[0].Life); Assert.AreEqual(12, s.Players[1].Life);
        }
        [Test] public void FiftySeedRandomLegalGamesTerminateAndConserveCards()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var s = GameEngine.Create(new RulesConfig(), db, db.Precons[seed % 2], db.Precons[1 - seed % 2], "fuzz" + seed); var rng = new KairosRng("choices" + seed); var list = new List<Command>();
                for (int step = 0; step < 1500 && s.Outcome == Outcome.Ongoing; step++)
                {
                    int p = GameEngine.NextActor(s); GameEngine.LegalCommands(s, p, list); Assert.IsNotEmpty(list, "seed " + seed); GameEngine.Apply(s, list[rng.Range(list.Count)]);
                    foreach (var player in s.Players) { Assert.LessOrEqual(player.Hand.Count, 8); Assert.LessOrEqual(player.Board.Count, 5); Assert.That(player.Marker, Is.InRange(0, 64)); }
                    var ids = new List<int>(); foreach (var player in s.Players) { ids.AddRange(player.Hand.Select(x => x.Id)); ids.AddRange(player.Deck.Select(x => x.Id)); ids.AddRange(player.Past.Select(x => x.Id)); ids.AddRange(player.Sigils.Select(x => x.Id)); ids.AddRange(player.Board.Select(x => x.Card.Id)); } ids.AddRange(s.Timeline.Select(x => x.Card.Id));
                    foreach (var f in s.InFlight) if (f.Card != null && !f.Countered && !ids.Contains(f.Card.Id)) ids.Add(f.Card.Id);
                    Assert.AreEqual(ids.Count, ids.Distinct().Count(), "duplicate card seed " + seed);
                }
                Assert.AreNotEqual(Outcome.Ongoing, s.Outcome, "seed " + seed);
            }
        }
    }
}
