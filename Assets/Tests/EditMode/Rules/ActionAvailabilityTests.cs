using System;
using System.IO;
using Kairos.Engine;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class ActionAvailabilityTests
    {
        CardDatabase db;
        [OneTimeSetUp] public void Load()
        {
            string path = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(path, "docs/data/cards.json"))) path = Directory.GetParent(path)?.FullName ?? throw new Exception("Missing data");
            db = CardDatabase.Load(File.ReadAllText(Path.Combine(path, "docs/data/cards.json")));
        }
        GameState State(int own = 0, int other = 1)
        {
            var s = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "availability");
            foreach (var p in s.Players) { p.Hand.Clear(); p.MulliganDone = true; }
            s.Players[0].Marker = own; s.Players[1].Marker = other; s.Players[0].LastMoveSeq = 10; return s;
        }
        CardInstance Card(GameState s, string key, int player = 0)
        { var c = new CardInstance { Id = s.NextId++, Owner = player, Key = key }; s.Players[player].Hand.Add(c); return c; }
        ActionAvailability Window(GameState s, int viewer = 0) => ActionAvailability.For(ViewRedactor.For(s, viewer));
        [Test] public void LagAndTieExplainOwnershipNotTurns()
        {
            var s = State(1, 2); Assert.AreEqual(ActionWindow.Act, Window(s).Window);
            s.Players[0].Marker = 2; StringAssert.Contains("后到", Window(s).Explanation);
            s.Players[1].LastMoveSeq = 11; Assert.AreEqual(ActionWindow.Wait, Window(s).Window);
            StringAssert.Contains("对手后到", Window(s).Explanation);
        }
        [Test] public void ResponseAndWaitingForResponseDifferWithoutLeakingHand()
        {
            var s = State(); var scout = Card(s, "DAWN_SCOUT"); Card(s, "DAWN_FIRST_LIGHT_STRIKE", 1);
            GameEngine.Apply(s, new Command { Player = 0, Kind = CommandKind.Play, CardId = scout.Id });
            var v = ViewRedactor.For(s, 0); Assert.AreEqual(ActionWindow.WaitResponse, ActionAvailability.For(v).Window);
            Assert.IsNull(v.Pending); Assert.IsEmpty(v.Players[1].Hand);
            Assert.AreEqual(PendingKind.Interject, v.PublicDecisionKind);
            Assert.AreEqual(ActionWindow.Respond, Window(s, 1).Window);
        }
        [Test] public void ChooseAndWaitingForChoiceDifferWithoutOptionsLeaking()
        {
            var s = State(); var c = Card(s, "N_GEAR_APPRENTICE");
            GameEngine.Apply(s, new Command { Player = 0, Kind = CommandKind.Play, CardId = c.Id });
            Assert.AreEqual(ActionWindow.Choose, Window(s).Window);
            var enemy = ViewRedactor.For(s, 1); Assert.AreEqual(ActionWindow.WaitChoice, ActionAvailability.For(enemy).Window);
            Assert.IsNull(enemy.Pending); Assert.IsEmpty(enemy.Players[0].Deck);
        }
        [Test] public void MulliganFinishedAndEndedAreNotOrdinaryActions()
        {
            var s = State(); s.Players[0].MulliganDone = false;
            Assert.AreEqual(ActionWindow.Mulligan, Window(s).Window);
            s.Players[0].MulliganDone = true; s.Players[0].Marker = 64; s.Players[0].Finished = true;
            Assert.AreEqual(ActionWindow.Finished, Window(s).Window);
            s.Outcome = Outcome.P1Win; Assert.AreEqual(ActionWindow.Ended, Window(s).Window);
        }
        [Test] public void CardBadgeDistinguishesPlayCoverSandAndNoLegalAction()
        {
            var s = State(); var cheap = Card(s, "DAWN_SCOUT"); var expensive = Card(s, "N_BRASS_GOLEM");
            var v = ViewRedactor.For(s, 0);
            Assert.IsTrue(ActionAvailability.Card(v, cheap.Id).CanPlay);
            var blocked = ActionAvailability.Card(v, expensive.Id); Assert.IsFalse(blocked.CanPlay); Assert.IsTrue(blocked.CanCover);
            s.Players[0].Sand = 2; var discounted = ActionAvailability.Card(ViewRedactor.For(s, 0), expensive.Id);
            Assert.IsTrue(discounted.CanPlay); Assert.AreEqual(2, discounted.MinSand);
            s.Players[0].Marker = 63; s.Players[1].Marker = 64; s.Players[1].Finished = true; s.Players[0].Sand = 0;
            var end = ActionAvailability.Card(ViewRedactor.For(s, 0), expensive.Id); Assert.IsFalse(end.CanPlay); Assert.IsFalse(end.CanCover);
        }
        [Test] public void ResponseOnlyHighlightsLegalResponsesAndDoesNotCover()
        {
            var s = State(); var scout = Card(s, "DAWN_SCOUT"); var reply = Card(s, "DAWN_FIRST_LIGHT_STRIKE", 1); var ordinary = Card(s, "N_BELL_GUARD", 1);
            GameEngine.Apply(s, new Command { Player = 0, Kind = CommandKind.Play, CardId = scout.Id });
            var v = ViewRedactor.For(s, 1);
            Assert.IsTrue(ActionAvailability.Card(v, reply.Id).CanPlay);
            Assert.IsFalse(ActionAvailability.Card(v, ordinary.Id).CanPlay);
            Assert.IsFalse(ActionAvailability.Card(v, ordinary.Id).CanCover);
        }
        [Test] public void AvailabilityDoesNotMutateStateAndDoesNotConfuseWaitingWithDeadCards()
        {
            var s = State(5, 1); var card = Card(s, "DAWN_SCOUT"); string before = JsonConvert.SerializeObject(s);
            var v = ViewRedactor.For(s, 0); var a = ActionAvailability.Card(v, card.Id);
            Assert.IsFalse(a.CanPlay); StringAssert.Contains("对手", a.Reason);
            Assert.AreEqual(before, JsonConvert.SerializeObject(s));
        }
    }
}
