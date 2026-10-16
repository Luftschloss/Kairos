using System;
using System.IO;
using Kairos.Engine;
using Kairos.Ai;
using Newtonsoft.Json;

internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            var db = CardDatabase.Load(File.ReadAllText("docs/data/cards.json"));
            int games = args.Length > 0 ? int.Parse(args[0]) : 20, wins = 0, total = 0;
            for (int i = 0; i < games; i++)
            {
                var state = GameEngine.Create(new RulesConfig(), db, db.Precons[0], db.Precons[1], "demo-" + i);
                int steps = 0;
                while (state.Outcome == Outcome.Ongoing && steps++ < 1200)
                {
                    int p = GameEngine.NextActor(state); var c = GreedyAi.Choose(ViewRedactor.For(state, p), db);
                    if (c == null) throw new Exception("No command at " + i + "/" + steps);
                    GameEngine.Apply(state, c);
                }
                if (state.Outcome == Outcome.Ongoing) throw new Exception("Non-terminating game " + i);
                var replay = new ReplayFile { Version = db.Version, Seed = state.Seed, Config = new RulesConfig(), Decks = new[] { db.Precons[0], db.Precons[1] }, Commands = state.Commands };
                var other = ReplayFile.FromJson(replay.ToJson()).Play(db);
                if (JsonConvert.SerializeObject(state.Players) != JsonConvert.SerializeObject(other.Players) || state.Outcome != other.Outcome) throw new Exception("Replay mismatch " + i);
                if (i == 0)
                {
                    string snapshot = JsonConvert.SerializeObject(new { state.Players, state.Timeline, state.Pending, state.InFlight, state.Frames, state.NextId, state.Rng, state.Outcome, state.LastDeclaredActor });
                    using (var sha = System.Security.Cryptography.SHA256.Create()) Console.WriteLine("seed0-state-sha256=" + BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(snapshot))).Replace("-", ""));
                }
                if (state.Outcome == Outcome.P0Win) wins++; total += steps;
                Console.WriteLine("seed=" + i + " outcome=" + state.Outcome + " commands=" + steps);
            }
            Console.WriteLine("PASS " + games + " complete games + deterministic replays; P0 wins=" + wins + "; mean commands=" + total / games + "; cards=" + db.Cards.Count + "; keepers=" + db.Keepers.Count);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
