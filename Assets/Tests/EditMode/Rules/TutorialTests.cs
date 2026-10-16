using System;
using System.IO;
using System.Linq;
using Kairos.Engine;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Kairos.Tests
{
    public sealed class TutorialTests
    {
        CardDatabase db;
        [OneTimeSetUp] public void Load()
        {
            string path = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(path, "docs/data/cards.json"))) path = Directory.GetParent(path)?.FullName ?? throw new Exception("Cannot locate cards.json");
            db = CardDatabase.Load(File.ReadAllText(Path.Combine(path, "docs/data/cards.json")));
        }
        [Test] public void LessonIsAnExplicitFixedPuzzleNotNormalSetup()
        {
            var lesson = TutorialLesson.Create(db); var s = lesson.State;
            Assert.IsTrue(s.MainPhase); Assert.AreEqual(0, s.Players[0].Marker); Assert.AreEqual(2, s.Players[1].Marker);
            Assert.AreEqual(4, s.Players[1].Life); Assert.IsTrue(s.Players[1].RefluxUsed);
            Assert.AreEqual(2, s.Players[0].Hand.Count); Assert.AreEqual(0, GameEngine.NextActor(s));
            var normal = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "normal");
            Assert.IsFalse(normal.MainPhase); Assert.AreEqual(20, normal.Players[1].Life); Assert.AreEqual(0, normal.Players[1].Marker); Assert.IsFalse(normal.Players[1].RefluxUsed);
        }
        [Test] public void FiveStepsTeachRealMovementCombatChimeAndVictory()
        {
            var lesson = TutorialLesson.Create(db); var s = lesson.State;
            lesson.Apply(lesson.ExpectedCommand());
            Assert.AreEqual(TutorialStep.FirstAttack, lesson.Step); Assert.AreEqual(1, s.Players[0].Marker); Assert.IsFalse(s.Players[0].Board[0].Exhausted);
            lesson.Apply(lesson.ExpectedCommand());
            Assert.AreEqual(TutorialStep.Chime, lesson.Step); Assert.AreEqual(2, s.Players[0].Marker); Assert.AreEqual(2, s.Players[1].Life); Assert.AreEqual(0, GameEngine.NextActor(s)); Assert.IsTrue(s.Players[0].Board[0].Exhausted);
            var events = lesson.Apply(lesson.ExpectedCommand());
            Assert.AreEqual(1, events.Count(e => e.Kind == "Chime")); Assert.AreEqual(3, s.Players[0].Hand.Count); Assert.IsFalse(s.Players[0].Board[0].Exhausted);
            Assert.AreEqual(3, GameEngine.Horizon(s, 0)); Assert.AreEqual(4, s.Players[0].Marker); Assert.AreEqual(1, GameEngine.NextActor(s));
            lesson.Apply(lesson.ExpectedCommand()); Assert.AreEqual(5, s.Players[1].Marker); Assert.AreEqual(0, GameEngine.NextActor(s));
            lesson.Apply(lesson.ExpectedCommand()); Assert.IsTrue(lesson.Complete); Assert.AreEqual(Outcome.P0Win, s.Outcome); Assert.AreEqual(5, s.Commands.Count); Assert.IsNull(lesson.ExpectedCommand());
        }
        [Test] public void UnexpectedActionCannotAdvanceLessonOrMutateGame()
        {
            var lesson = TutorialLesson.Create(db); string before = JsonConvert.SerializeObject(lesson.State.Players);
            Assert.Throws<RulesViolationException>(() => lesson.Apply(new Command { Player = 0, Kind = CommandKind.Wait }));
            Assert.AreEqual(TutorialStep.Deploy, lesson.Step); Assert.AreEqual(0, lesson.State.Commands.Count); Assert.AreEqual(before, JsonConvert.SerializeObject(lesson.State.Players));
            Assert.IsFalse(lesson.Allows(null));
        }
        [Test] public void WrongAttackTargetCannotBypassTeachingGate()
        {
            var lesson = TutorialLesson.Create(db); lesson.Apply(lesson.ExpectedCommand());
            var bad = lesson.ExpectedCommand(); bad.Target = new Target(TargetKind.Keeper, 0);
            Assert.IsFalse(lesson.Allows(bad)); Assert.Throws<RulesViolationException>(() => lesson.Apply(bad));
            Assert.AreEqual(20, lesson.State.Players[0].Life); Assert.AreEqual(TutorialStep.FirstAttack, lesson.Step);
        }
        [Test] public void TutorialUsesNoRandomAiOrResponseTimers()
        {
            var lesson = TutorialLesson.Create(db);
            while (!lesson.Complete)
            {
                var expected = lesson.ExpectedCommand(); Assert.IsNull(GameEngine.IllegalReason(lesson.State, expected));
                lesson.Apply(expected); Assert.IsNull(lesson.State.Pending);
            }
        }
        [Test] public void RestartCreatesFreshIndependentFixture()
        {
            var a = TutorialLesson.Create(db); var b = TutorialLesson.Create(db);
            a.Apply(a.ExpectedCommand()); Assert.AreEqual(TutorialStep.Deploy, b.Step); Assert.AreEqual(0, b.State.Players[0].Board.Count);
            Assert.AreEqual(0, b.State.Players[0].Marker); Assert.AreEqual(2, b.State.Players[0].Hand.Count);
        }
        [Test] public void TutorialCommandSequenceCanBeReplayedAgainstSameFixture()
        {
            var a = TutorialLesson.Create(db); while (!a.Complete) a.Apply(a.ExpectedCommand());
            var b = TutorialLesson.Create(db); foreach (var c in a.State.Commands) b.Apply(c);
            Assert.AreEqual(JsonConvert.SerializeObject(a.State.Players), JsonConvert.SerializeObject(b.State.Players)); Assert.AreEqual(a.State.Outcome, b.State.Outcome);
        }
    }
}
