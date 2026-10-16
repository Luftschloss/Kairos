using System;
using System.IO;
using Kairos.Engine;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class ActionPreviewTests
    {
        CardDatabase db;
        [OneTimeSetUp] public void Load()
        {
            string path = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(path, "docs/data/cards.json"))) path = Directory.GetParent(path)?.FullName ?? throw new Exception("Missing card data");
            db = CardDatabase.Load(File.ReadAllText(Path.Combine(path, "docs/data/cards.json")));
        }
        GameState State(int own, int other)
        {
            var s = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "preview");
            foreach (var p in s.Players) { p.Hand.Clear(); p.MulliganDone = true; }
            s.Players[0].Marker = own; s.Players[1].Marker = other; s.Players[0].LastMoveSeq = 10;
            return s;
        }
        Command Card(GameState s, string key, CommandKind kind = CommandKind.Play)
        {
            var card = new CardInstance { Id = s.NextId++, Owner = 0, Key = key }; s.Players[0].Hand.Add(card);
            return new Command { Player = 0, Kind = kind, CardId = card.Id };
        }
        ActionPreview Preview(GameState s, Command c) => ActionPreview.For(ViewRedactor.For(s, 0), c, db, GameEngine.Cost(s, c));
        [TestCase(1, 2, 0)] [TestCase(2, 2, 1)]
        public void PaymentPredictsLaterArrivalAndHandoff(int own, int other, int expected)
        { var s = State(own, other); Assert.AreEqual(expected, Preview(s, Card(s, "DAWN_SCOUT")).NextActor); }
        [Test] public void NewChimesOnlyAndNoMutation()
        {
            var s = State(3, 8); s.Players[0].ChimesRung = 1; s.LastDeclaredActor = 0;
            string before = JsonConvert.SerializeObject(s);
            var p = Preview(s, new Command { Player = 0, Kind = CommandKind.Wait });
            Assert.AreEqual(9, p.Landing); CollectionAssert.AreEqual(new[] { 8 }, p.Chimes); Assert.IsTrue(p.Chain);
            Assert.AreEqual(before, JsonConvert.SerializeObject(s));
        }
        [Test] public void EndIsNotKairosOrChime()
        {
            var s = State(63, 64); s.Players[0].ChimesRung = 15; s.Players[1].Finished = true;
            var p = Preview(s, Card(s, "DAWN_SCOUT")); Assert.AreEqual(-1, p.NextActor); Assert.IsFalse(p.Kairos); Assert.IsEmpty(p.Chimes);
        }
        [Test] public void OmenPositionUsesPostPaymentNotMarkerLandingClamp()
        {
            var s = State(3, 7); var c = Card(s, "DAWN_SCOUT", CommandKind.Omen); c.Offset = 3;
            Assert.AreEqual(8, Preview(s, c).FuturePosition); Assert.IsNull(GameEngine.IllegalReason(s, c));
        }
        [Test] public void ChannelUsesPrintedDelayAndLockedTarget()
        {
            var s = State(3, 7); CardDefinition channel = null;
            foreach (var d in db.Cards) if (d.Channel.HasValue) { channel = d; break; }
            Assert.NotNull(channel); var c = Card(s, channel.Key);
            Assert.AreEqual(3 + GameEngine.Cost(s, c) + channel.Channel.Value, Preview(s, c).FuturePosition);
        }
        [Test] public void CrossingDoesNotCountOpposingChannelOrRevealOmen()
        {
            var s = State(3, 8);
            for (int i = 0; i < 3; i++) s.Timeline.Add(new TimelineObject { Id = 100 + i, Owner = i == 2 ? 0 : 1, Pos = 4, Kind = i == 0 ? ObjectKind.Omen : ObjectKind.Channel, Card = new CardInstance { Key = "DAWN_SCOUT", Owner = 1 }, Context = new EffectContext() });
            var v = ViewRedactor.For(s, 0); Assert.IsNull(v.Timeline[0].Card);
            var p = ActionPreview.For(v, new Command { Player = 0, Kind = CommandKind.Focus }, db, 2);
            CollectionAssert.AreEquivalent(new[] { 100, 102 }, p.CrossingObjects); Assert.IsNull(v.Timeline[0].Card);
        }
        [Test] public void ResponseExposesOnlyDeclaredCardAndDefersNextActor()
        {
            var s = State(0, 0); var c = Card(s, "DAWN_SCOUT");
            s.Players[1].Hand.Add(new CardInstance { Id = s.NextId++, Owner = 1, Key = "DAWN_FIRST_LIGHT_STRIKE" });
            GameEngine.Apply(s, c); var v = ViewRedactor.For(s, 1);
            Assert.NotNull(v.Declaration); Assert.AreEqual("DAWN_SCOUT", v.Declaration.Card.Key); Assert.IsEmpty(v.Players[0].Hand);
            var response = new Command { Player = 1, Kind = CommandKind.Interject, CardId = s.Players[1].Hand[0].Id, Target = new Target(TargetKind.Keeper, 0) };
            Assert.AreEqual(-2, ActionPreview.For(v, response, db, GameEngine.Cost(s, response)).NextActor);
        }
        [Test] public void PreviewCostIncludesSandAndKairos()
        {
            var s = State(3, 7); s.Players[0].Sand = 1; var c = Card(s, "N_BELL_GUARD"); c.Sand = 1;
            var p = Preview(s, c); Assert.AreEqual(1, p.Cost); Assert.AreEqual(4, p.Landing); Assert.IsTrue(p.Kairos);
        }
    }
}
