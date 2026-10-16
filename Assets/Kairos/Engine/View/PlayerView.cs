using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Kairos.Engine
{
    public sealed class PlayerView
    {
        public int Viewer, Actor, LastDeclaredActor;
        // Only an already-public declaration is projected, never an undeclared hand card.
        public InFlightAction Declaration;
        public Outcome Outcome;
        public bool MainPhase;
        public RulesConfig Config;
        public PlayerState[] Players;
        public int[] HandCounts, DeckCounts;
        public List<TimelineObject> Timeline;
        public PendingDecision Pending;
        // Public phase only; choice options and hidden cards stay private to their owner.
        public PendingKind? PublicDecisionKind;
        public List<GameEvent> Events;
        public List<Command> Legal = new List<Command>();
        public Dictionary<string, int> Costs = new Dictionary<string, int>();
        public string TopCard;
        public int Cost(Command command) => Costs.TryGetValue(CommandKey(command), out int cost) ? cost : 0;
        public static string CommandKey(Command c) => c.Kind + ":" + c.CardId + ":" + c.UnitId + ":" + c.Sand;
    }
    public static class ViewRedactor
    {
        // No engine state reference escapes: AI and presentation receive only this projection.
        public static PlayerView For(GameState s, int viewer)
        {
            var v = new PlayerView { Viewer = viewer, Actor = GameEngine.NextActor(s), Outcome = s.Outcome, Config = s.Config.Clone(), MainPhase = s.MainPhase, Players = new PlayerState[2], HandCounts = new int[2], DeckCounts = new int[2], Timeline = new List<TimelineObject>(), Events = new List<GameEvent>() };
            for (int p = 0; p < 2; p++)
            {
                v.HandCounts[p] = s.Players[p].Hand.Count; v.DeckCounts[p] = s.Players[p].Deck.Count;
                v.Players[p] = s.Players[p].Clone(); v.Players[p].Deck.Clear();
                if (p != viewer) v.Players[p].Hand.Clear();
            }
            foreach (var a in s.Db.Keeper(s.Players[viewer].Keeper).Static) if (a.Kind == "REVEAL_TOP_TO_OWNER" && s.Players[viewer].Deck.Count > 0) v.TopCard = s.Players[viewer].Deck[0].Key;
            foreach (var o in s.Timeline) { var copy = o.Clone(); if (o.Kind == ObjectKind.Omen && o.Owner != viewer) { copy.Card = null; copy.Context = null; } v.Timeline.Add(copy); }
            v.PublicDecisionKind = s.Pending?.Kind;
            if (s.Pending != null && s.Pending.Player == viewer)
            {
                v.Pending = s.Pending.Clone();
                if (s.Pending.Kind == PendingKind.Pick || s.Pending.Kind == PendingKind.Scry)
                    foreach (int id in s.Pending.Options) v.Players[viewer].Deck.Add(GameEngine.FindCard(s.Players[viewer].Deck, id).Clone());
            }
            v.LastDeclaredActor = s.LastDeclaredActor;
            if (s.Pending?.Kind == PendingKind.Interject && s.InFlight.Count > 0)
                v.Declaration = s.InFlight[s.InFlight.Count - 1].Clone();
            foreach (var e in s.Events) if (e.PrivateTo < 0 || e.PrivateTo == viewer) v.Events.Add(e.Clone());
            GameEngine.LegalCommands(s, viewer, v.Legal);
            foreach (var c in v.Legal) if (c.Kind <= CommandKind.Interject && c.Kind != CommandKind.Mulligan) v.Costs[PlayerView.CommandKey(c)] = GameEngine.Cost(s, c);
            return v;
        }
    }
    public sealed class MarkerPreview
    {
        public int FinalMarker, OpponentWindow;
        public bool Kairos;
        public List<int> Chimes = new List<int>(), Omens = new List<int>();
        public static MarkerPreview For(PlayerView v, Command c)
        {
            int p = c.Player, landing = v.Players[p].Marker + v.Cost(c);
            var result = new MarkerPreview { FinalMarker = Math.Min(v.Config.TimelineEnd, landing), OpponentWindow = Math.Max(0, landing - v.Players[1 - p].Marker), Kairos = landing >= v.Config.BarLength && landing <= v.Config.KairosLineMax && landing % v.Config.BarLength == 0 };
            for (int line = (v.Players[p].ChimesRung + 1) * v.Config.BarLength; line <= result.FinalMarker && line < v.Config.TimelineEnd; line += v.Config.BarLength) result.Chimes.Add(line);
            foreach (var o in v.Timeline) if (o.Kind == ObjectKind.Omen && o.Pos <= result.FinalMarker && o.Pos > v.Players[p].Marker) result.Omens.Add(o.Pos);
            return result;
        }
    }
    public sealed class ReplayFile
    {
        public string Version, Seed;
        public RulesConfig Config;
        public DeckList[] Decks;
        public List<Command> Commands;
        public bool Campaign;
        public int[] InitialLife;
        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
        public static ReplayFile FromJson(string json) => JsonConvert.DeserializeObject<ReplayFile>(json);
        public GameState Play(CardDatabase db)
        {
            Rules.Require(Version == db.Version, "R-17-2", "回放与卡池版本不匹配");
            var state = GameEngine.Create(Config, db, Decks[0], Decks[1], Seed, Campaign);
            if (InitialLife != null && InitialLife.Length == 2) for (int p = 0; p < 2; p++) state.Players[p].Life = InitialLife[p];
            foreach (var c in Commands) GameEngine.Apply(state, c); return state;
        }
    }
}
