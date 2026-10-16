using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public enum TutorialStep { Deploy, FirstAttack, Chime, OpponentWait, FinalAttack, Complete }

    // A deterministic puzzle fixture, not a second ruleset. All actions use GameEngine.Apply.
    // The training dummy starts at tick 2, with 4 life and its one reflux already spent.
    // Normal matches still use the unchanged setup, decks, mulligan and 20 starting life.
    public sealed class TutorialLesson
    {
        public GameState State { get; private set; }
        public TutorialStep Step { get; private set; }
        public int ScoutCardId { get; private set; }
        public bool Complete => Step == TutorialStep.Complete;

        public static TutorialLesson Create(CardDatabase db)
        {
            var state = new GameState { Config = new RulesConfig(), Db = db, Seed = "kairos-tutorial-v1", Rng = new KairosRng("kairos-tutorial-v1") };
            state.Players[0] = new PlayerState { Id = 0, Keeper = "K_DAWN_IRIN", Life = 20, Marker = 0, LastMoveSeq = 1, MulliganDone = true };
            state.Players[1] = new PlayerState { Id = 1, Keeper = "K_NOON_ORIS", Life = 4, Marker = 2, MulliganDone = true, RefluxUsed = true };
            var lesson = new TutorialLesson { State = state };
            var scout = Card(state, 0, "DAWN_SCOUT"); state.Players[0].Hand.Add(scout); lesson.ScoutCardId = scout.Id;
            // Keep a visibly unavailable 3-cost card to explain the initial horizon of 2.
            state.Players[0].Hand.Add(Card(state, 0, "N_WANDERING_TIMEKEEPER"));
            for (int i = 0; i < 8; i++)
            {
                state.Players[0].Deck.Add(Card(state, 0, i == 0 ? "N_HOURGLASS" : "N_BELL_GUARD"));
                state.Players[1].Deck.Add(Card(state, 1, "N_BRASS_GOLEM"));
            }
            state.Events.Add(new GameEvent { Kind = "Tutorial", Text = "教学残局：你在 0 刻，训练钟偶在 2 刻；钟偶仅 4 命烛，逆流已用。" });
            return lesson;
        }
        static CardInstance Card(GameState s, int p, string key) => new CardInstance { Id = s.NextId++, Owner = p, Key = key };
        public int ScoutUnitId
        {
            get { foreach (var u in State.Players[0].Board) if (u.Card.Id == ScoutCardId) return u.Id; return 0; }
        }
        public Command ExpectedCommand()
        {
            switch (Step)
            {
                case TutorialStep.Deploy: return new Command { Player = 0, Kind = CommandKind.Play, CardId = ScoutCardId };
                case TutorialStep.FirstAttack: case TutorialStep.FinalAttack:
                    return new Command { Player = 0, Kind = CommandKind.Attack, UnitId = ScoutUnitId, Target = new Target(TargetKind.Keeper, 1) };
                case TutorialStep.Chime: return new Command { Player = 0, Kind = CommandKind.Focus };
                case TutorialStep.OpponentWait: return new Command { Player = 1, Kind = CommandKind.Wait };
                default: return null;
            }
        }
        public bool Allows(Command c)
        {
            var expected = ExpectedCommand();
            return c != null && expected != null && c.Kind == expected.Kind && c.Player == expected.Player && c.CardId == expected.CardId && c.UnitId == expected.UnitId && c.Target.Equals(expected.Target) && c.Sand == 0;
        }
        public IReadOnlyList<GameEvent> Apply(Command c)
        {
            Rules.Require(Allows(c), "TUTORIAL", "请先完成当前教学目标；其他行动可在自由对局中尝试。");
            var events = GameEngine.Apply(State, c);
            Step = (TutorialStep)((int)Step + 1);
            if (Complete) Rules.Require(State.Outcome == Outcome.P0Win, "TUTORIAL", "教学未达到预期终局");
            return events;
        }
    }
}
